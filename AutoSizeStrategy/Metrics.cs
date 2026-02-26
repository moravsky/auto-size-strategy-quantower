using System;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public record AccountMetrics(
        double DrawdownRemaining,
        int TradesToClutchMode,
        int TradesToBust
    );

    public class Metrics(IStrategySettings settings)
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
                return new AccountMetrics(0, 0, 0);

            double availableDrawdown = GetAvailableDrawdown(account);
            if (availableDrawdown <= 0)
                return new AccountMetrics(0, 0, 0);

            double liquidationThreshold = account.Balance - availableDrawdown;
            double clutchTriggerBalance = _settings.ClutchModeTriggerBalance;

            var (stopTicks, tickVal, lossPerContract) = GetUnitEconomics();
            if (!double.IsFinite(tickVal) || tickVal <= 0)
                return new AccountMetrics(availableDrawdown, 0, 0);

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
                DrawdownRemaining: availableDrawdown,
                TradesToClutchMode: tradesToClutch,
                TradesToBust: tradesToClutch + clutchTrades
            );
        }

        private double GetAvailableDrawdown(IAccount account)
        {
            var mode = account.InferDrawdownMode();
            return RiskCalculator.GetAvailableDrawdown(
                account,
                mode,
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
                if (currentRiskPct == 1)
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
