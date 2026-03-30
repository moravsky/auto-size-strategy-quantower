using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public partial class AutoSizeStrategy
    {
        IAccount IStrategySettings.CurrentAccount =>
            CurrentAccount != null ? new AccountWrapper(CurrentAccount) : null;

        public double RiskPercent { get; set; } = 2.5;

        public MissingStopLossAction MissingStopLossAction { get; set; } =
            MissingStopLossAction.Reject;

        public double MinAccountBalanceOverride { get; set; }
        public int MinimumStopLossTicks { get; set; } = 20;
        public double CommissionMicro { get; set; } = 0.25;
        public double CommissionMini { get; set; } = 2.5;
        public double AverageSlippageTicks { get; set; } = 1.5;
        public double ClutchModeBudget { get; set; } = 1350.0;
        public double[] ClutchModeRisk { get; set; } = [0.25, 0.25, 1.00];
        public int MaxContractsMicro { get; set; } = 150;
        public int MaxContractsMini { get; set; } = 15;
        public DrawdownMode DrawdownMode { get; set; } = DrawdownMode.Static;
        private string _accountId = Core.Accounts.FindTargetAccount()?.Id;
        private readonly List<SettingItem> _additionalSettings = [];

        public Account CurrentAccount
        {
            get => Core.Accounts.FirstOrDefault(a => a.Id == _accountId, field);
            set
            {
                _accountId = value?.Id;
                field = value;
            }
        }

        public override IList<SettingItem> Settings
        {
            get
            {
                var result = base.Settings;
                result.AddRange(_additionalSettings);
                return result;
            }
        }

        private void InitializeSettings()
        {
            InitializeRiskManagementGroup();
            InitializeAccountLongevityGroup();
            InitializeExecutionCostsGroup();
        }

        private void InitializeRiskManagementGroup()
        {
            var drawdownModeVariants = new List<SelectItem>
            {
                new("Intraday (Trailing)", DrawdownMode.Intraday),
                new("End of Day", DrawdownMode.EndOfDay),
                new("Static", DrawdownMode.Static),
            };

            DrawdownMode = CurrentAccount != null
                ? new AccountWrapper(CurrentAccount).InferDrawdownMode()
                : DrawdownMode.Static;

            var drawdownModeSetting = new SettingItemSelectorLocalized(
                "Drawdown Mode",
                drawdownModeVariants.First(v => (DrawdownMode)v.Value == DrawdownMode),
                drawdownModeVariants
            )
            {
                Description = """
                              How available risk capital is calculated.

                              Intraday (Trailing): Uses the broker's trailing drawdown threshold
                              (e.g. TPT PRO accounts). Risk capital = Balance - AutoLiquidateThreshold.

                              End of Day: Uses Minimum Balance Override as the floor.
                              Risk capital = Balance - Minimum Balance Override.

                              Static: Threshold does not move.
                              Suitable for personal/cash accounts with no drawdown rules.
                              """,
            };
            drawdownModeSetting.PropertyChanged += (_, _) =>
            {
                if (drawdownModeSetting.Value is SelectItem si)
                    this.DrawdownMode = (DrawdownMode)si.Value;
            };

            var accountSetting = new SettingItemAccount("Account", this.CurrentAccount);
            accountSetting.PropertyChanged += (_, _) =>
            {
                CurrentAccount = accountSetting.Value as Account;
                DrawdownMode = ((IStrategySettings)this).CurrentAccount?.InferDrawdownMode()
                               ?? DrawdownMode.Static;

                var matchingItem = drawdownModeVariants.FirstOrDefault(v => (DrawdownMode)v.Value == DrawdownMode);
                if (matchingItem != null)
                {
                    drawdownModeSetting.Value = matchingItem;
                }
            };

            var riskPercentSetting = new SettingItemDouble("Risk Percent", this.RiskPercent)
            {
                Minimum = 0.1,
                Maximum = 100.0,
                Increment = 0.1,
                DecimalPlaces = 1,
                Description = """
                              Risk capital allocated per trade as a percentage of available risk capital / drawdown.
                              
                              Example: $150,000 static account at 8% -> $12,000 risk budget per trade.
                              """
            };
            riskPercentSetting.PropertyChanged += (_, _) =>
                this.RiskPercent = (double)riskPercentSetting.Value;

            var variants = new List<SelectItem>
            {
                new("Reject", MissingStopLossAction.Reject),
                new("Ignore", MissingStopLossAction.Ignore),
            };
            var missingStopLossActionSetting = new SettingItemSelectorLocalized(
                "Action on Missing Stop Loss",
                variants.First(),
                variants
            )
            {
                Description = """
                              Determines behavior when an order is placed without a Stop Loss.

                              Reject: Instantly cancels the order.
                              Ignore: Allows the order to pass to the broker without applying
                              the auto-size risk math.
                              """,
            };
            missingStopLossActionSetting.PropertyChanged += (_, _) =>
            {
                if (missingStopLossActionSetting.Value is SelectItem si)
                    this.MissingStopLossAction = (MissingStopLossAction)si.Value;
            };

            var minBalanceSetting = new SettingItemDouble(
                "Minimum Balance Override",
                this.MinAccountBalanceOverride
            )
            {
                Minimum = 0.0,
                Increment = 0.01,
                DecimalPlaces = 2,
                Description = """
                              Required for End of Day (EOD) drawdown accounts.
                              Sets the hard floor balance where the account is busted.

                              Note: For Intraday or Static accounts, this overrides
                              the broker's threshold.
                              """,
            };
            minBalanceSetting.PropertyChanged += (_, _) =>
                this.MinAccountBalanceOverride = (double)minBalanceSetting.Value;

            var maxContractsMicroSetting = new SettingItemInteger(
                "Max Contracts (Micro)",
                this.MaxContractsMicro
            )
            {
                Minimum = 0,
                Increment = 1,
                Description = """
                              Hard cap on position size for micro contracts (MNQ, MGC, etc.).
                              Prevents excessive sizing from tight stops or large account balances.
                              Set to 0 to disable.
                              """,
            };
            maxContractsMicroSetting.PropertyChanged += (_, _) =>
                this.MaxContractsMicro = (int)maxContractsMicroSetting.Value;

            var maxContractsMiniSetting = new SettingItemInteger(
                "Max Contracts (Mini)",
                this.MaxContractsMini
            )
            {
                Minimum = 0,
                Increment = 1,
                Description = """
                              Hard cap on position size for mini contracts (NQ, GC, etc.).
                              Prevents excessive sizing from tight stops or large account balances.
                              Set to 0 to disable.
                              """,
            };
            maxContractsMiniSetting.PropertyChanged += (_, _) =>
                this.MaxContractsMini = (int)maxContractsMiniSetting.Value;

            _additionalSettings.Add(
                new SettingItemGroup(
                    "Risk Management",
                    [
                        accountSetting,
                        drawdownModeSetting,
                        riskPercentSetting,
                        missingStopLossActionSetting,
                        minBalanceSetting,
                        maxContractsMicroSetting,
                        maxContractsMiniSetting,
                    ]
                )
            );
        }

        private void InitializeAccountLongevityGroup()
        {
            var stopLossSetting = new SettingItemInteger("Initial Stop Loss Ticks", this.MinimumStopLossTicks)
            {
                Minimum = 1,
                Increment = 1,
                Description = """
                              Minimum stop distance used for metrics calculations before your first trade.

                              No stop order is placed by this setting — it is used only to estimate
                              trades-to-bust and similar metrics at startup. Once you place a real order
                              with a stop loss, your stop distance is used instead.
                              """,
            };
            stopLossSetting.PropertyChanged += (_, _) =>
                this.MinimumStopLossTicks = (int)stopLossSetting.Value;

            var slippageSetting = new SettingItemDouble(
                "Average Slippage Ticks",
                this.AverageSlippageTicks
            )
            {
                Minimum = 0.0,
                Increment = 0.1,
                DecimalPlaces = 1,
            };
            slippageSetting.PropertyChanged += (_, _) =>
                this.AverageSlippageTicks = (double)slippageSetting.Value;

            var commMicroSetting = new SettingItemDouble(
                "Commission Micro (Per Side)",
                this.CommissionMicro
            )
            {
                Minimum = 0.0,
                Increment = 0.01,
                DecimalPlaces = 2,
            };
            commMicroSetting.PropertyChanged += (_, _) =>
                this.CommissionMicro = (double)commMicroSetting.Value;

            var commMiniSetting = new SettingItemDouble(
                "Commission Mini (Per Side)",
                this.CommissionMini
            )
            {
                Minimum = 0.0,
                Increment = 0.01,
                DecimalPlaces = 2,
            };
            commMiniSetting.PropertyChanged += (_, _) =>
                this.CommissionMini = (double)commMiniSetting.Value;

            var clutchModeBudgetSetting = new SettingItemDouble(
                "Clutch Mode Budget",
                this.ClutchModeBudget
            )
            {
                Minimum = 0.0,
                Increment = 0.01,
                DecimalPlaces = 2,
                Description = """
                              The dollar amount reserved for 'Clutch Mode' — a sequence of elevated-risk
                              trades designed to recover from a deep drawdown.

                              Set to 0 to disable. With Clutch Mode off, 'Trades to Bust' reflects
                              pure consecutive max losers at your target risk percent.
                              """,
            };
            clutchModeBudgetSetting.PropertyChanged += (_, _) =>
                this.ClutchModeBudget = (double)clutchModeBudgetSetting.Value;

            var clutchModeRiskSequenceSetting = new SettingItemString(
                "Clutch Mode Risk Sequence",
                string.Join(", ", ClutchModeRisk)
            )
            {
                Description = """
                              A comma-separated list of risk multipliers used during Clutch Mode.
                              Values must be between 0.01 and 1.0.

                              Example: '0.25, 0.25, 1.0' means the first two trades risk 25%
                              of the clutch budget, and the final trade risks the rest.
                              """,
            };
            clutchModeRiskSequenceSetting.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(SettingItem.Value))
                    return;
                if (
                    TryParseClutchSequence(
                        clutchModeRiskSequenceSetting.Value as string,
                        out double[] parsed
                    )
                )
                {
                    ClutchModeRisk = parsed;
                }
            };

            _additionalSettings.Add(
                new SettingItemGroup(
                    "Account Longevity",
                    [
                        stopLossSetting,
                        clutchModeBudgetSetting,
                        clutchModeRiskSequenceSetting,
                    ]
                )
            );
        }

        public static bool TryParseClutchSequence(string input, out double[] clutchSequence)
        {
            clutchSequence = [];
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var parts = input.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (parts.Length == 0)
                return false;

            clutchSequence = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], out double val) || val <= 0 || val > 1.0)
                    return false;
                clutchSequence[i] = val;
            }

            return true;
        }

        private void InitializeExecutionCostsGroup()
        {
            var slippageSetting = new SettingItemDouble("Average Slippage Ticks", this.AverageSlippageTicks)
            {
                Minimum = 0.0,
                Increment = 0.1,
                DecimalPlaces = 1,
            };
            slippageSetting.PropertyChanged += (_, _) => this.AverageSlippageTicks = (double)slippageSetting.Value;

            var commMicroSetting = new SettingItemDouble("Commission Micro (Per Side)", this.CommissionMicro)
            {
                Minimum = 0.0,
                Increment = 0.01,
                DecimalPlaces = 2,
            };
            commMicroSetting.PropertyChanged += (_, _) => this.CommissionMicro = (double)commMicroSetting.Value;

            var commMiniSetting = new SettingItemDouble("Commission Mini (Per Side)", this.CommissionMini)
            {
                Minimum = 0.0,
                Increment = 0.01,
                DecimalPlaces = 2,
            };
            commMiniSetting.PropertyChanged += (_, _) => this.CommissionMini = (double)commMiniSetting.Value;

            _additionalSettings.Add(
                new SettingItemGroup(
                    "Execution Costs", // <--- Creates the new tab/group
                    [
                        slippageSetting,
                        commMicroSetting,
                        commMiniSetting,
                    ]
                )
            );
        }
    }
}
