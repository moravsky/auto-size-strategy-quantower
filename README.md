# AutoSizeStrategy

![Platform](https://img.shields.io/badge/Platform-Quantower-blue)
![.NET Runtime](https://img.shields.io/badge/.NET%20Runtime-8.0-512BD4)
![.NET SDK](https://img.shields.io/badge/.NET%20SDK-10.0-512BD4)

Focus on your entries, exits, and stops. Let AutoSizeStrategy handle the futures position sizing.

A Quantower order-placing strategy that automatically sizes every order to your exact risk - accounting for stop distance, slippage, and commissions.

---

## Features

- **Stop-distance-aware sizing** - set your risk as percent of capital, place your order bracket. Contract count is calculated automatically every time.
- **Dynamic Cancel/Replace** - If you modify stop loss on a working limit order, the strategy intercepts it, recalculates your risk for the new distance, and seamlessly replaces the order with the correct new size.
- **Position-aware scaling & reversals** - the engine reads your open net position. It safely clamps add-on trades so you never exceed your max risk, accurately sizes full position reversals, and does not resize exit orders so you can scale out freely.
- **Prop firm drawdown modes** - supports trailing intraday, end-of-day, and static cash accounts.
- **Custom Balance Floor** - The strategy auto-detects `AutoLiquidateThresholdCurrentValue`, but allows you to set a manual Minimum Balance Override to provide your balance floor on any connection, account type, or personal cash account.
- **Account longevity metrics** - The metrics panel calculates exactly how many consecutive trades you can lose before busting the account. If enabled, it factors in your "Clutch Mode" risk sequence to provide a realistic survival gauge.
- **Value at Risk** - live absolute and relative VaR displayed in the Quantower metrics panel, updated every second.
- **Max contracts cap** - hard position size limit per instrument class (micro/mini) as a backstop against tight stops or large balances.

---

## Requirements

**To run:**
- [Quantower](https://www.quantower.com/) v1.145.17+ with an active connection (any futures broker)
- Windows 10+ x64, or macOS via UTM (ARM Windows x64 emulation)
- .NET 8 x64 Runtime (bundled with Quantower)

**To build from source:**
- .NET 10 x64 SDK

---

## Installation

**Option 1: Installer (recommended)**

Download `AutoSizeStrategy-Setup-x.x.x.exe` from [Releases](https://github.com/moravsky/auto-size-strategy/releases). 
Run the installer - it auto-detects your Quantower installation path.
Restart Quantower.

*Note: Windows Defender SmartScreen may flag the installer as an unrecognized app since it is a new, unsigned release. Click "More info" and then "Run anyway" to proceed with the installation.*

**Option 2: Manual Install (ZIP)**
1. Download `AutoSizeStrategy-2.0.0.zip` from Releases.
2. **Crucial:** Right-click the downloaded `.zip` -> **Properties** -> check the **Unblock** box at the bottom -> click **OK**. *(Windows silently blocks downloaded plugins by default).*
3. Extract the contents into `C:\Quantower\Settings\Scripts\Strategies\AutoSizeStrategy`.
4. Restart Quantower.

**Option 3: Build from source**

```shell
git clone https://github.com/moravsky/auto-size-strategy.git
cd auto-size-strategy
.\deploy.ps1 -Config Release
```

## Quick Start

1. Open Quantower and connect to your broker.
2. Go to **Strategies Manager** -> find **AutoSizeStrategy42** -> click **Run**.
3. In the settings panel, configure:
   - **Account** - select your trading account
   - **Drawdown Mode** - select your account's drawdown rule type
   - **Minimum Balance Override** - set your account's bust threshold
   - **Risk Percent** - percentage of available risk capital per trade
4. Place an order with a stop loss bracket attached. The strategy resizes it automatically.

---

## Settings Reference

### Risk Management

| Setting | Description |
|---|---|
| Account | Target account to size orders for |
| Drawdown Mode | How risk capital is calculated - see [How It Works](#how-it-works) |
| Risk Percent | % of available risk capital to risk per trade |
| Action on Missing Stop Loss | `Reject` cancels orders with no SL. `Ignore` passes them through unsized - all sizing logic is disabled for that order, including the Max Contracts cap |
| Minimum Balance Override | The balance at which your account is blown. Required for EOD accounts. Acts as a manual "hard deck" override for all other modes. |
| Max Contracts (Micro) | Hard cap for micro contracts (MNQ, MGC, etc.). `0` = disabled |
| Max Contracts (Mini) | Hard cap for mini contracts (NQ, GC, etc.). `0` = disabled |

*Note: Minimum Balance Override does not cause actual position liquidation. It is used for metric calculation only.*

### Account Longevity

| Setting | Description |
|---|---|
| Initial Stop Loss Ticks | Default stop distance used for metrics before your first trade. No order is placed - metrics only |
| Clutch Mode Budget | Dollar amount reserved for the Clutch Mode sequence shown in metrics. `0` = disabled |
| Clutch Mode Risk Sequence | Comma-separated risk multipliers for each Clutch trade. Default: `0.25, 0.25, 1.0` |

### Execution Costs

| Setting | Description |
|---|---|
| Average Slippage Ticks | Expected slippage per side, used in sizing math |
| Commission Micro (Per Side) | Commission per contract per side for micros |
| Commission Mini (Per Side) | Commission per contract per side for minis |

---

## How It Works

### Drawdown Modes

**Intraday (Trailing)** - for accounts with a trailing drawdown threshold (e.g. TPT PRO). Risk capital = `Balance - AutoLiquidateThreshold`. As your balance grows, the threshold follows, keeping your risk capital window fixed.

**End of Day** - for accounts where the drawdown floor is fixed per session (e.g. TPT Eval). Risk capital = `Balance - Minimum Balance Override`. Set the override to the exact balance at which the account is busted.

**Static** - for cash or personal accounts with no drawdown rules. Risk capital = full account balance.

### Account Longevity Metrics & Clutch Mode

Standard risk calculators often produce an unrealistically high "Trades to Bust" metric because they assume you will robotically risk your standard percentage (e.g., 2%) all the way down to zero.

In reality, when a trader nears their drawdown limit, they rarely do this. Instead, they usually switch gears and take a few calculated, higher-risk swings to try and recover.

**Clutch Mode** was built to model this real-world behavior so your metrics are actually accurate. It reserves a specific dollar amount (your `Clutch Mode Budget`) for a predefined recovery sequence (e.g., `0.25, 0.25, 1.0`).

This creates two distinct metrics on your Quantower panel:
* **Trades to Clutch:** The number of consecutive normal losing trades you can take before your balance drops into your Clutch Mode Budget (the "danger zone").
* **Trades to Bust:** The absolute total number of consecutive losing trades before your account is blown (Normal trades + Clutch trades).

*Note: This is a metric calculation only to help you structure your remaining budget; the strategy does not automatically change your actual risk input.*

### Value at Risk (VaR) Metrics

Absolute and Relative VaR are live metrics that show your exact, real-time capital exposure across all open positions.

* **Absolute VaR:** Your total open risk in dollars. It is calculated dynamically based on your exact entry prices and the exact locations of your working stop-loss orders (baking in your configured slippage and commissions).
* **Relative VaR:** Your total open risk expressed as a percentage of your total available risk capital.

If you scale out of a position partially or move your stop loss, your VaR instantly changes to reflect your new risk. If you accidentally leave any open position unprotected (no stop loss or does not cover full position), VaR immediately spikes to 100% of your max account exposure.

---

## Known Issues

### Brief warning flash on stop loss modification (Rithmic live accounts)

When AutoSizeStrategy resizes an order via Cancel/Replace, a brief red warning ("Atomic order operation in progress") may appear in the order log. This is cosmetic only - the Cancel/Replace completes correctly and the resized order is placed. The flash occurs because Quantower does not currently respect CancellationToken on requests.

---

## Author

**Petr Moravsky** ([petr@structuredtrading.co](mailto:petr@structuredtrading.co)) — futures trader and developer.

If AutoSizeStrategy helped you pass an eval, size live trades, or served as a reference for your own development — [star the repo](https://github.com/moravsky/auto-size-strategy) and [leave a tip](https://ko-fi.com/moravsky).