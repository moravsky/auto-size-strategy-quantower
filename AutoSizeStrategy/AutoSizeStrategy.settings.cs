using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public partial class AutoSizeStrategy
    {
        public Account CurrentAccount { get; set; } = Core.Accounts.FindTargetAccount();
        IAccount IStrategySettings.CurrentAccount =>
            CurrentAccount != null ? new AccountWrapper(CurrentAccount) : null;

        public double RiskPercent { get; set; } = 8.0;
        public MissingStopLossAction MissingStopLossAction { get; set; } =
            MissingStopLossAction.Reject;
        public double MinAccountBalanceOverride { get; set; } = 0.0;
        public int MinimumStopLossTicks { get; set; } = 20;
        public double CommissionMicro { get; set; } = 0.25;
        public double CommissionMini { get; set; } = 2.5;
        public double AverageSlippageTicks { get; set; } = 1.5;
        public double ClutchModeTriggerPercent { get; set; } = 30.0;
        public double[] ClutchModeRisk { get; set; } = [0.25, 0.25, 1.00];
        private readonly List<SettingItem> _additionalSettings = [];

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
        }

        private void InitializeRiskManagementGroup()
        {
            var accountSetting = new SettingItemAccount("Account", this.CurrentAccount);
            accountSetting.PropertyChanged += (s, e) =>
                this.CurrentAccount = accountSetting.Value as Account;

            var riskPercentSetting = new SettingItemDouble("Risk Percent", this.RiskPercent)
            {
                Minimum = 0.1,
                Maximum = 100.0,
                Increment = 0.1,
                DecimalPlaces = 1,
            };
            riskPercentSetting.PropertyChanged += (s, e) =>
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
            );
            missingStopLossActionSetting.PropertyChanged += (s, e) =>
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
            };
            minBalanceSetting.PropertyChanged += (s, e) =>
                this.MinAccountBalanceOverride = (double)minBalanceSetting.Value;

            _additionalSettings.Add(
                new SettingItemGroup(
                    "Risk Management",
                    [
                        accountSetting,
                        riskPercentSetting,
                        missingStopLossActionSetting,
                        minBalanceSetting,
                    ]
                )
            );
        }

        private void InitializeAccountLongevityGroup()
        {
            var stopLossSetting = new SettingItemInteger(
                "Initial Stop Loss Ticks",
                this.MinimumStopLossTicks
            )
            {
                Minimum = 1,
                Increment = 1,
            };
            stopLossSetting.PropertyChanged += (s, e) =>
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
            slippageSetting.PropertyChanged += (s, e) =>
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
            commMicroSetting.PropertyChanged += (s, e) =>
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
            commMiniSetting.PropertyChanged += (s, e) =>
                this.CommissionMini = (double)commMiniSetting.Value;

            var clutchModeTriggerSetting = new SettingItemDouble(
                "Clutch Mode Trigger Percent",
                this.ClutchModeTriggerPercent
            )
            {
                Minimum = 0.0,
                Maximum = 100.0,
                Increment = 1.0,
            };
            clutchModeTriggerSetting.PropertyChanged += (s, e) =>
                this.ClutchModeTriggerPercent = (double)clutchModeTriggerSetting.Value;

            var clutchModeRiskSequenceSetting = new SettingItemString(
                "Clutch Mode Risk Sequence",
                string.Join(", ", ClutchModeRisk)
            );
            clutchModeRiskSequenceSetting.PropertyChanged += (s, e) =>
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
                        slippageSetting,
                        commMicroSetting,
                        commMiniSetting,
                        clutchModeTriggerSetting,
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
    }
}
