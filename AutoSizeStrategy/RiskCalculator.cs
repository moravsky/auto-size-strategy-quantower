using System;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    /// <summary>
    /// Indicates the type of drawdown to use when calculating available risk capital.
    /// </summary>
    public enum DrawdownMode
    {
        Intraday,
        EndOfDay,
        Static,
    }

    /* * CAUTIONARY TALE: THE $104 MILLION PENNY
    * On April 20, 2020, Oil futures went to -$37.63.
    * Interactive Brokers' software assumed prices couldn't go below zero
    * and displayed the price as $0.01.
    * * Result: Users bought the "dip" at negative prices, yet the prices kept going lower.
    * * The liquidation engine failed to trigger, and IBKR lost ~$104 million covering client debts.
    * * LESSON: NEVER assume values (Prices, Balances, Risk) are always positive.
    * Always validate inputs and handle "impossible" edge cases.
    */
    public static class RiskCalculator
    {
        public static double CalculateCostPerContract(
            double stopDistanceTicks,
            double tickValue,
            double slippageTicks,
            double commission
        )
        {
            MathUtil.ValidateFinite(stopDistanceTicks, nameof(stopDistanceTicks));
            MathUtil.ValidateFinite(tickValue, nameof(tickValue));
            MathUtil.ValidateFinite(slippageTicks, nameof(slippageTicks));
            MathUtil.ValidateFinite(commission, nameof(commission));

            if (tickValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickValue), "Tick value must be positive");

            if (stopDistanceTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(stopDistanceTicks), "Stop distance must be > 0");

            if (slippageTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(slippageTicks), "Slippage cannot be negative");

            if (commission < 0)
                throw new ArgumentOutOfRangeException(nameof(commission), "Commission cannot be negative");

            return (stopDistanceTicks + slippageTicks) * tickValue + commission;
        }

        public static int CalculatePositionSize(
            double positionRisk,
            double costPerContract
        )
        {
            MathUtil.ValidateFinite(positionRisk, nameof(positionRisk));
            MathUtil.ValidateFinite(costPerContract, nameof(costPerContract));

            if (positionRisk <= 0 || costPerContract <= 0)
            {
                return 0;
            }

            double rawResult = positionRisk / costPerContract;
            return (int)Math.Floor(rawResult + MathUtil.Epsilon);
        }

        public static double GetStopDistanceTicks(
            SlTpHolder slTpHolder,
            double tickSize,
            double entryPrice
        )
        {
            MathUtil.ValidateFinite(slTpHolder.Price, nameof(slTpHolder) + ".Price");
            MathUtil.ValidateFinite(tickSize, nameof(tickSize));
            MathUtil.ValidateFinite(entryPrice, nameof(entryPrice));

            if (tickSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickSize), "Tick size must be positive");
            }

            if (slTpHolder.PriceMeasurement == PriceMeasurement.Offset)
            {
                return slTpHolder.Price;
            }
            else if (slTpHolder.PriceMeasurement == PriceMeasurement.Absolute)
            {
                return Math.Round(
                    Math.Abs(entryPrice - slTpHolder.Price) / tickSize,
                    MidpointRounding.AwayFromZero
                );
            }
            else
            {
                throw new ArgumentException("Price measurement must be Offset or Absolute");
            }
        }

        public static double GetAvailableRiskCapital(
            IAccount account,
            DrawdownMode mode,
            out string reason,
            double minAccountBalanceOverride = 0.0
        )
        {
            MathUtil.ValidateFinite(minAccountBalanceOverride, nameof(minAccountBalanceOverride));
            ArgumentNullException.ThrowIfNull(account);

            double availableRiskCapital;
            reason = "";

            if (minAccountBalanceOverride > 0)
            {
                reason =
                    $"Using minAccountBalanceOverride={minAccountBalanceOverride} for drawdown calculation";
                availableRiskCapital = account.Balance - minAccountBalanceOverride;
            }
            else
            {
                switch (mode)
                {
                    case DrawdownMode.Static:
                        availableRiskCapital = account.Balance;
                        break;
                    case DrawdownMode.Intraday:
                        if (
                            !account.TryGetInfoDouble(
                                "AutoLiquidateThresholdCurrentValue",
                                out double autoLiqCurrent
                            )
                        )
                        {
                            reason = "Missing 'AutoLiquidateThresholdCurrentValue' in Account Info";
                            return 0;
                        }

                        availableRiskCapital = account.Balance - autoLiqCurrent;
                        break;
                    case DrawdownMode.EndOfDay:
                        reason = "EOD mode requires 'Minimum Account Balance (Override)' to be set";
                        return 0;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(mode),
                            "Unsupported drawdown mode."
                        );
                }
            }

            if (availableRiskCapital <= 0)
            {
                reason = $"Available risk capital is zero or negative ({availableRiskCapital})";
                return 0;
            }

            if (availableRiskCapital > account.Balance)
            {
                availableRiskCapital = account.Balance;
                reason = "Risk capital capped at Account Balance (Calculation exceeded balance)";
            }

            if (string.IsNullOrEmpty(reason))
            {
                reason = $"OK Risk Capital: {availableRiskCapital:F2}";
            }

            return availableRiskCapital;
        }

        public static double CalculatePositionRisk(
            IAccount account,
            double riskPercent,
            DrawdownMode mode,
            out string reason,
            double minAccountBalanceOverride = 0.0
        )
        {
            MathUtil.ValidateFinite(riskPercent, nameof(riskPercent));

            if (riskPercent <= 0 || riskPercent > 100)
                throw new ArgumentOutOfRangeException(
                    nameof(riskPercent),
                    "Risk percentage must be > 0 and <= 100."
                );

            double availableRiskCapital = GetAvailableRiskCapital(
                account,
                mode,
                out reason,
                minAccountBalanceOverride
            );

            if (availableRiskCapital <= 0)
                return 0;

            double positionRisk = availableRiskCapital * (riskPercent / 100.0);
            reason = $"OK Drawdown: {availableRiskCapital:F2}, Risk: {positionRisk:F2}";
            return positionRisk;
        }
    }
}