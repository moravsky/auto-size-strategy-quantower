using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public record AccountMetrics(
        double? RiskCapital,
        int? TradesToClutchMode,
        int? TradesToBust,
        double? AbsoluteValueAtRisk,
        double? RelativeValueAtRiskPercent
    );

    public class Metrics(
        IStrategySettings settings,
        Func<IAccount, IEnumerable<IPosition>> positionProvider = null,
        Func<IAccount, IEnumerable<IOrder>> workingOrderProvider = null)
    {
        private const int MaxIterations = 10_000;
        private const double MinRiskPercentage = .05;

        private readonly IStrategySettings _settings =
            settings ?? throw new ArgumentNullException(nameof(settings));

        private readonly Func<IAccount, IEnumerable<IPosition>> _positionProvider =
            positionProvider ?? (_ => []);

        private readonly Func<IAccount, IEnumerable<IOrder>> _workingOrderProvider =
            workingOrderProvider ?? (_ => []);

        public ISymbol LastSymbol { get; set; } = new DefaultSymbol();
        public double LastStopDistanceTicks { get; set; } = settings.MinimumStopLossTicks;

        public AccountMetrics GetAccountMetrics()
        {
            var account = _settings.CurrentAccount;
            if (account == null)
                return new AccountMetrics(null, null, null, null, null);

            double availableDrawdown = GetAvailableDrawdown(account);
            if (availableDrawdown <= 0)
                return new AccountMetrics(0, 0, 0, 0, 0);

            var (stopTicks, tickVal, lossPerContract) = GetUnitEconomics();
            if (!double.IsFinite(tickVal) || tickVal <= 0)
                return new AccountMetrics(availableDrawdown, null, null, null, null);

            var (absVaR, relVaR) = CalculateValueAtRisk(account);
            double liquidationThreshold = account.Balance - availableDrawdown;
            double clutchTriggerBalance = liquidationThreshold + _settings.ClutchModeBudget;
            if (clutchTriggerBalance <= 0)
                return new AccountMetrics(availableDrawdown, null, null, absVaR, relVaR);

            int tradesToClutch = GetNormalTrades(
                startBalance: account.Balance,
                endBalance: clutchTriggerBalance,
                hardFloor: liquidationThreshold,
                stopTicks,
                tickVal,
                lossPerContract,
                _settings.RiskPercent / 100.0
            );

            int clutchTrades = GetClutchTrades(
                currentBalance: account.Balance,
                clutchTriggerBalance,
                hardFloor: liquidationThreshold,
                stopTicks,
                tickVal,
                lossPerContract
            );


            return new AccountMetrics(
                RiskCapital: availableDrawdown,
                TradesToClutchMode: tradesToClutch,
                TradesToBust: tradesToClutch + clutchTrades,
                AbsoluteValueAtRisk: absVaR,
                RelativeValueAtRiskPercent: relVaR
            );
        }

        // Distance to broker liquidation if known, otherwise full balance
        private static double GetMaxExposure(IAccount account)
        {
            return account.TryGetInfoDouble("AutoLiquidateThresholdCurrentValue", out double threshold)
                ? account.Balance - threshold
                : account.Balance;
        }

        private (double absolute, double relativePct) CalculateValueAtRisk(
            IAccount account)
        {
            if (account == null)
                return (double.NaN, double.NaN);

            double maxExposure = GetMaxExposure(account);
            double totalAbsolute = 0;
            var positions = _positionProvider(account).ToList();
            var workingOrders = _workingOrderProvider(account).ToList();

            foreach (var pos in positions)
            {
                double tickSize = pos.Symbol.TickSize;
                double tickValue = pos.Symbol.GetTickCost(pos.OpenPrice);

                if (!double.IsFinite(tickValue) || tickValue <= 0 || tickSize <= 0)
                {
                    return (maxExposure, 100.0);
                }

                // Find all protective stops for this position
                var stopOrders = workingOrders.Where(o =>
                        o.Symbol.Id == pos.Symbol.Id
                        && o.Side != pos.Side
                        && (o.OrderTypeId == OrderType.Stop
                            || o.OrderTypeId == OrderType.StopLimit
                            || o.OrderTypeId == OrderType.TrailingStop))
                    // Order so the closest protective stops (least risk) are applied first
                    .OrderByDescending(o => pos.Side == Side.Buy ? o.GetLikelyFillPrice() : -o.GetLikelyFillPrice())
                    .ToList();

                double unprotectedQty = pos.Quantity;

                foreach (var stop in stopOrders)
                {
                    if (unprotectedQty <= MathUtil.Epsilon)
                        break;

                    double expectedExitPrice = stop.GetLikelyFillPrice();
                    double distanceTicks = Math.Abs(pos.OpenPrice - expectedExitPrice) / tickSize;

                    // Cap the calculated quantity to the remaining unprotected amount
                    double protectedQty = Math.Min(stop.TotalQuantity, unprotectedQty);
                    double slippageCost = _settings.AverageSlippageTicks * tickValue * protectedQty;

                    totalAbsolute += (distanceTicks * tickValue * protectedQty) + slippageCost;
                    unprotectedQty -= protectedQty;
                }

                if (unprotectedQty > MathUtil.Epsilon)
                {
                    // If any portion of the position is unprotected, it bounds to max broker exposure
                    return (maxExposure, 100.0);
                }
            }

            double absolute = Math.Min(totalAbsolute, maxExposure);
            double relativePct = maxExposure > 0 ? (absolute / maxExposure) * 100.0 : 0;
            return (absolute, relativePct);
        }

        private double GetAvailableDrawdown(IAccount account)
        {
            return RiskCalculator.GetAvailableDrawdown(
                account,
                _settings.DrawdownMode,
                out _,
                minAccountBalanceOverride: _settings.MinAccountBalanceOverride
            );
        }

        private (double stopTicks, double tickVal, double lossPerContract) GetUnitEconomics()
        {
            double stopTicks = Math.Max(LastStopDistanceTicks, _settings.MinimumStopLossTicks);
            double tickVal = LastSymbol.GetTickCost(LastSymbol.Last);

            if (double.IsNaN(tickVal) || tickVal <= 0)
                return (stopTicks, 0, 0);

            double commission = LastSymbol.IsMicro()
                ? _settings.CommissionMicro
                : _settings.CommissionMini;

            // Loss includes slippage and round-trip commissions
            double loss = (stopTicks + _settings.AverageSlippageTicks) * tickVal + (commission * 2);

            return (stopTicks, tickVal, loss);
        }

        // Simulate each shot losing from the clutch trigger to get zone boundaries.
        // The zone current balance falls into tells us how many shots are left.
        private int GetClutchTrades(
            double currentBalance,
            double clutchTriggerBalance,
            double hardFloor,
            double stopTicks,
            double tickVal,
            double lossPerContract
        )
        {
            double zoneFloor = clutchTriggerBalance;
            double[] clutchSequence = _settings.ClutchModeRisk;

            for (int i = 0; i < clutchSequence.Length; i++)
            {
                double riskBase = zoneFloor - hardFloor;

                // Let RiskCalculator determine the true size. If it's 0, this shot
                // yields 0 contracts, meaning the floor won't move for this iteration.
                int contracts = RiskCalculator.CalculatePositionSize(
                    riskBase * clutchSequence[i],
                    stopTicks,
                    tickVal
                );

                zoneFloor -= contracts * lossPerContract;

                // Above the post-loss threshold: we're inside shot i's zone, it hasn't fired yet.
                if (currentBalance > zoneFloor)
                    return clutchSequence.Length - i;
            }

            return 0;
        }

        private static int GetNormalTrades(
            double startBalance,
            double endBalance,
            double hardFloor,
            double stopTicks,
            double tickVal,
            double lossPerContract,
            params double[] riskLevels
        )
        {
            if (startBalance <= endBalance)
                return 0;
            if (stopTicks <= 0 || tickVal <= 0 || lossPerContract <= 0)
                return 0;
            if (riskLevels == null || riskLevels.Length == 0)
                return 0;

            double currentBalance = startBalance;

            for (int trades = 0; trades < MaxIterations; trades++)
            {
                if (currentBalance <= endBalance)
                    return trades;

                if (currentBalance <= hardFloor)
                    return trades;

                double currentRiskPct =
                    trades < riskLevels.Length ? riskLevels[trades] : riskLevels[^1];
                if (MathUtil.Equals(currentRiskPct, 1))
                    return trades + 1;

                double riskBase = currentBalance - hardFloor;

                if (riskBase <= (startBalance - endBalance) * MinRiskPercentage)
                    return trades;

                double riskDollars = riskBase * currentRiskPct;
                int contracts = RiskCalculator.CalculatePositionSize(
                    riskDollars,
                    stopTicks,
                    tickVal
                );
                if (contracts == 0)
                    return trades;
                currentBalance -= contracts * lossPerContract;
            }

            return MaxIterations;
        }
    }
}
