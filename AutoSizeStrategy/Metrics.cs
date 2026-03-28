using System;
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
        ITradingService tradingService = null)
    {
        private const int MaxIterations = 10_000;
        private const double MinRiskPercentage = .05;

        private readonly IStrategySettings _settings =
            settings ?? throw new ArgumentNullException(nameof(settings));

        public ISymbol LastSymbol { get; set; } = new DefaultSymbol();
        public double LastStopDistanceTicks { get; set; } = settings.MinimumStopLossTicks;

        public AccountMetrics GetAccountMetrics()
        {
            var account = _settings.CurrentAccount;
            if (account == null)
                return new AccountMetrics(null, null, null, null, null);

            double availableRiskCapital = RiskCalculator.GetAvailableRiskCapital(
                account,
                _settings.DrawdownMode,
                out _,
                minAccountBalanceOverride: _settings.MinAccountBalanceOverride
            );

            if (availableRiskCapital <= 0)
                return new AccountMetrics(0, 0, 0, 0, 0);

            var costPerContract = GetCostPerContract();
            if (costPerContract <= 0)
                return new AccountMetrics(availableRiskCapital, null, null, null, null);

            var (absVaR, relVaR) = CalculateValueAtRisk(account, availableRiskCapital);
            double liquidationThreshold = account.Balance - availableRiskCapital;
            double clutchTriggerBalance = liquidationThreshold + _settings.ClutchModeBudget;

            if (clutchTriggerBalance <= 0)
                return new AccountMetrics(availableRiskCapital, null, null, absVaR, relVaR);

            int tradesToClutch = GetNormalTrades(
                startBalance: account.Balance,
                endBalance: clutchTriggerBalance,
                hardFloor: liquidationThreshold,
                costPerContract,
                _settings.RiskPercent / 100.0
            );

            int clutchTrades = GetClutchTrades(
                currentBalance: account.Balance,
                clutchTriggerBalance,
                hardFloor: liquidationThreshold,
                costPerContract
            );

            return new AccountMetrics(
                RiskCapital: availableRiskCapital,
                TradesToClutchMode: tradesToClutch,
                TradesToBust: tradesToClutch + clutchTrades,
                AbsoluteValueAtRisk: absVaR,
                RelativeValueAtRiskPercent: relVaR
            );
        }

        private (double absolute, double relativePct) CalculateValueAtRisk(IAccount account, double availableRiskCapital)
        {
            if (account == null)
                return (double.NaN, double.NaN);

            double totalAbsolute = 0;

            var positions = tradingService?.GetPositions(account).ToList() ?? [];
            var workingOrders = tradingService?.GetWorkingOrders(account).ToList() ?? [];

            foreach (var pos in positions)
            {
                double tickSize = pos.Symbol.TickSize;
                double tickValue = pos.Symbol.GetTickCost(pos.OpenPrice);

                if (!double.IsFinite(tickValue) || tickValue <= 0 || tickSize <= 0)
                {
                    return (availableRiskCapital, 100.0);
                }

                // Find all protective stops for this position
                var stopOrders = workingOrders.Where(o =>
                        o.Symbol.Id == pos.Symbol.Id
                        && o.Side != pos.Side
                        && (o.OrderTypeId == OrderType.Stop
                            || o.OrderTypeId == OrderType.StopLimit
                            || o.OrderTypeId == OrderType.TrailingStop))
                    .OrderByDescending(o => pos.Side == Side.Buy ? o.GetLikelyFillPrice() : -o.GetLikelyFillPrice())
                    .ToList();

                double unprotectedQty = pos.Quantity;

                foreach (var stop in stopOrders)
                {
                    if (unprotectedQty <= MathUtil.Epsilon)
                        break;

                    double expectedExitPrice = stop.GetLikelyFillPrice();
                    double distanceTicks = Math.Abs(pos.OpenPrice - expectedExitPrice) / tickSize;
                    double protectedQty = Math.Min(stop.TotalQuantity, unprotectedQty);

                    double exitCostPerContract = RiskCalculator.CalculateCostPerContract(
                        distanceTicks,
                        tickValue,
                        _settings.AverageSlippageTicks,
                        _settings.GetCommission(pos.Symbol) // exit only, one side
                    );

                    totalAbsolute += exitCostPerContract * protectedQty;
                    unprotectedQty -= protectedQty;
                }

                if (unprotectedQty > MathUtil.Epsilon)
                {
                    return (availableRiskCapital, 100.0);
                }
            }

            double absolute = Math.Min(totalAbsolute, availableRiskCapital);
            double relativePct = availableRiskCapital > 0 ? (absolute / availableRiskCapital) * 100.0 : 0;
            return (absolute, relativePct);
        }

        private double GetCostPerContract()
        {
            double stopTicks = Math.Max(LastStopDistanceTicks, _settings.MinimumStopLossTicks);
            double tickVal = LastSymbol.GetTickCost(LastSymbol.Last);

            if (!double.IsFinite(tickVal) || tickVal <= 0)
                return 0;

            double commission = _settings.GetCommission(LastSymbol);
            return RiskCalculator.CalculateCostPerContract(
                stopTicks,
                tickVal,
                _settings.AverageSlippageTicks,
                commission * 2
            );
        }

        private int GetClutchTrades(
            double currentBalance,
            double clutchTriggerBalance,
            double hardFloor,
            double costPerContract
        )
        {
            double zoneFloor = clutchTriggerBalance;
            double[] clutchSequence = _settings.ClutchModeRisk;

            for (int i = 0; i < clutchSequence.Length; i++)
            {
                double riskBase = zoneFloor - hardFloor;

                int contracts = RiskCalculator.CalculatePositionSize(
                    riskBase * clutchSequence[i],
                    costPerContract
                );

                zoneFloor -= contracts * costPerContract;

                if (currentBalance > zoneFloor)
                    return clutchSequence.Length - i;
            }

            return 0;
        }

        private static int GetNormalTrades(
            double startBalance,
            double endBalance,
            double hardFloor,
            double costPerContract,
            params double[] riskLevels
        )
        {
            if (startBalance <= endBalance)
                return 0;
            if (costPerContract <= 0)
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
                    costPerContract
                );
                if (contracts == 0)
                    return trades;
                currentBalance -= contracts * costPerContract;
            }

            return MaxIterations;
        }
    }
}