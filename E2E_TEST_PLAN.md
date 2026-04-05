### AutoSizeStrategy Manual E2E Testing Guide

For major releases do all Part 1 tests in Replay environment and then on live connection.
For minor releases do all Part 1 tests in Replay environment and then Part 2 tests on live connection.
Replay and live connections behvior has subtle differences.

## Prerequisites

1.  Build the strategy in Release mode.
2.  Ensure DLL is copied to `C:\Quantower\Settings\Scripts\Strategies\AutoSizeStrategy`.
3.  Open **Connections -> Market Replay**. Add both **MNQ** and **NQ** symbols.
    Set a history range that covers regular trading hours (e.g., 6:30 AM - 9:30 AM).
4.  Have charts open for both **MNQ** and **NQ**.

-----

## Part 1: Extensive Test Suite

### Baseline Configuration

*The math for the following tests relies strictly on this configuration.*

1.  Connect to **Connections -> Replay** (use any historical data source).
2.  Set the starting account balance to **$150,000**.
3.  Go to **Strategies Manager -> AutoSizeStrategy42**.
4.  Configure the settings as follows:
      * **Target Account:** Select the Replay/Sim account
      * **Drawdown Mode:** End of Day *(Forces deterministic calculation)*
      * **Minimum Balance Override:** 145500 *(Risk Capital = $4,500)*
      * **Risk Percent:** 5.0% *(Risk Budget = $225)*
      * **Action on Missing Stop Loss:** Reject
      * **Initial Stop Loss Ticks:** 128
      * **Average Slippage Ticks:** 1.5 *(default)*
      * **Commission Micro:** 0.25
      * **Max Contracts (Micro):** 0 *(Disabled)*
      * **Clutch Mode Budget:** 1350

*Base Math Reference:*

  * MNQ Tick Value = $0.50
  * 128 Ticks = $64.00 Risk per contract
  * Overhead (1.5 Slip Ticks + $0.50 Round Trip Comm) = $1.25
  * Total Cost Per Contract = $65.25
  * Risk Budget ($225) / $65.25 = 3.44
  * **Base Target Size = 3 contracts**

-----

## Phase 1: Reject Mode (5%, No Caps)

*Tests that require Reject mode or only use resting limit orders (no position holding needed).*

### Test Suite A: Basic Order Resizing

*Pre-condition: Ensure Net Position is 0.*

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
|<input type="checkbox"> | Limit order with standard SL | 1. Open MNQ chart.<br>2. Set Order Qty to `1`.<br>3. Enable SL and set to `128` ticks.<br>4. Click **Buy Bid**. | Order appears on the chart with Qty `3`.<br>**Log:** "Changed request ... quantity from 1 to 3." |
|<input type="checkbox"> | Market order with standard SL | 1. Set Order Qty to `1`.<br>2. Enable SL to `128` ticks.<br>3. Click **Buy Market**. | Position opens with Qty `3`. |
|<input type="checkbox"> | Stop order with standard SL | 1. Set Order Qty to `1`.<br>2. Enable SL to `128` ticks.<br>3. Place a Buy Stop. | Pending Stop Order appears with Qty `3`. |
|<input type="checkbox"> | Large SL = Smaller Size | 1. Set Order Qty to `1`.<br>2. Change SL to `200` ticks.<br>3. Place a Buy Limit order. | Order Qty is `2`. *(225 / $101.25 total cost = 2.22)*<br>**Log:** "quantity from 1 to 2." |
|<input type="checkbox"> | Tight SL = Larger Size | 1. Set Order Qty to `1`.<br>2. Change SL to `40` ticks.<br>3. Place a Buy Limit order. | Order Qty is `10`. *(225 / $21.25 total cost = 10.58)*<br>**Log:** "quantity from 1 to 10." |

*(Clean up: Cancel all working orders and flatten positions before continuing).*

-----

### Stop Loss Rejection

*Pre-condition: Net Position is 0. Strategy setting `Action on Missing Stop Loss` = Reject.*

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
|<input type="checkbox"> | Reject Mode enforcement | 1. Disable SL entirely.<br>2. Place a Buy Limit order for Qty `1`. | Order flashes and disappears.<br>**Log:** "cancelled: stop loss required". |

*(Clean up: Cancel working orders if any).*

-----

### Test Suite D: Order Modification

*Pre-condition: Net Position is 0. SL enabled to 128 ticks.*

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
|<input type="checkbox"> | Expanding SL shrinks size | 1. Place Buy Limit Qty `1` (Sizes to `3`).<br>2. Right-click the order -> **Modify**.<br>3. Change SL offset to `200` ticks.<br>4. Click **Apply**. | Order cancels and replaces with Qty `2`. *(225 / $101.25 = 2.22)*<br>**Log:** "resizing order ... via Cancel/Replace". |
|<input type="checkbox"> | Moving Price only | 1. Left-click and drag the resting Buy Limit order down by 5 points on the chart.<br>2. Release mouse. | Order moves to new price. Qty remains `2`. Stop loss moves down with it to maintain 200-tick distance. |

*(Clean up: Cancel working orders).*

-----

## Phase 2: Ignore Mode (5%, No Caps)

*Stop Strategy. Change `Action on Missing Stop Loss` to **Ignore**. Start Strategy.*

*Ignore mode lets you cancel SLs after fill to keep positions open during testing. Orders placed WITH a stop loss are still sized normally.*

### Test Suite C: Exit Order Pass-Through

*Pre-condition: Enter a Long position of 3 contracts (e.g., Buy Market Qty 1 with 128-tick SL, then cancel SL).*

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
|<input type="checkbox"> | Exit with SL is ignored | 1. Enable SL to `128` ticks.<br>2. Place a Sell Limit order above market for Qty `2`. | Sell Limit is placed for Qty `2` (does not upsize to 3).<br>**Log:** "Passing through opposite side request". |
|<input type="checkbox"> | Exit without SL is ignored | 1. Disable SL.<br>2. Place a Sell Limit order above market for Qty `1`. | Sell Limit is placed for Qty `1`.<br>**Log:** "Passing through opposite side request". |

*(Clean up: Flatten positions).*

-----

### Test Suite F: Position-Aware Addition & Reversals

*Pre-condition: Net Position is 0. Risk Percent remains at **5%** (Budget = $225, Base Capacity = **3 contracts**).*

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
|<input type="checkbox"> | Existing position reduces capacity | 1. Place Buy Market Qty `1` with 128-tick SL (Fills `3` contracts).<br>2. Close `1` contract manually, leaving Net Position at Long `2`.<br>3. Place a new Buy Limit order Qty `1` with 128-tick SL. | Order is placed with Qty `1`. *(Capacity 3 - Existing 2 = 1)*.<br>**Log:** "Changed request ... from 1 to 1". |
|<input type="checkbox"> | Hard stop at max capacity | 1. Flatten position and cancel any working orders.<br>2. Click **MKT** (Buy) Qty `1` with 128-tick SL (Fills `3` contracts).<br>3. Place a new Buy Limit order Qty `1` with 128-tick SL. | The second order flashes and is immediately cancelled. Qty = 0.<br>**Log:** "position already at target size (3)". |
|<input type="checkbox"> | Position Flip / Reversal | 1. Ensure Net Position is Long `3`.<br>2. Set Order Quantity to `100`.<br>3. Click **MKT** (Sell) with 128-tick SL. | Sell Market order is sized to `6` contracts *(3 to flatten + 3 max new short exposure)*.<br>Net position instantly becomes Short `3`. |

*(Clean up: Flatten positions).*

-----

### Test Suite H: Value at Risk (VaR) Reporting

*Pre-condition: Strategy running. Risk Percent 5%. Balance $150,000. Override 145500. Expected Max Exposure = $4500.*

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
| <input type="checkbox"> | Single Protected Position | 1. Place Buy Limit Qty `1` with 128-tick SL (Sizes to `3`).<br>2. Allow order to fill.<br>3. Open Strategy Metrics panel. | **Absolute VaR:** `$195.00`. *(3 qty * 128 ticks * $0.50 tick value = $192.00 + $2.25 slip + $0.75 comms)*.<br>**Relative VaR:** `4.33%`. *(195 / 4500)* |
| <input type="checkbox"> | Modified SL updates VaR | 1. Drag the resting SL line on the chart down from 128 ticks to 200 ticks. | **Absolute VaR:** Jumps to `$303.00` within 1 second. *(3 * 200 * $0.50 = $300.00 + $2.25 slip + $0.75 comms)*. |
| <input type="checkbox"> | Partial Protection Spike | 1. Right-click the resting SL line on the chart -> **Modify**.<br>2. Change the order Quantity from `3` to `2` and click **Apply**.<br>*(This leaves 1 position contract completely unprotected).* | **Absolute VaR:** Spikes instantly to `$4500.00` (Max Exposure).<br>**Relative VaR:** Spikes to `100%`. |
| <input type="checkbox"> | Multiple Stops (Blended) | 1. With the position of `3` still open, right-click the chart 128 ticks (32 points) below the entry price.<br>2. Place a **Sell Stop** order for Qty `1`.<br>*(You now have 2 contracts protected at 200 ticks, and 1 contract protected at 128 ticks).* | **Absolute VaR:** Drops to `$267.00`.<br>*(Qty 2 @ 200 ticks + $1.50 slip + $0.50 comm = $202.00)*<br>*(Qty 1 @ 128 ticks + $0.75 slip + $0.25 comm = $65.00)*<br>**Relative VaR:** `5.93%`. *(267 / 4500)* |

*(Clean up: Flatten positions).*

-----

### Stop Loss Ignore & Cancel/Replace

*Pre-condition: Net Position is 0.*

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
|<input type="checkbox"> | Ignore Mode enforcement | 1. Place a Buy Limit order for Qty `1` with NO SL. | Order is successfully placed with Qty `1`.<br>**Log:** "has no stop loss - passing through unchanged". |
|<input type="checkbox"> | Cancel/Replace when SL added | 1. With the Qty `1` order from Ignore Mode enforcement resting, right-click it on the chart -> **Modify**.<br>2. Check "Stop Loss", set offset to `128` ticks.<br>3. Click **Apply**. | Original order cancels. New Limit order appears with Qty `3` and a 128-tick SL.<br>**Log:** "resizing order ... via Cancel/Replace". |

*(Clean up: Cancel all working orders and flatten positions).*

### Unprotected VaR

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
| <input type="checkbox"> | Unprotected Spike | 1. Click **NO SL** in Order Entry.<br>2. Click **MKT** (Buy) Qty `1`. | **Absolute VaR:** Spikes instantly to `$4500.00` (Max Exposure).<br>**Relative VaR:** Spikes to `100%`. |

*(Clean up: Flatten positions).*

-----

## Phase 3: Stress Sizing

*Stop Strategy. Change `Action on Missing Stop Loss` back to **Reject**. Set Risk Percent to **20%**. Set **Max Contracts (Micro)** to **5**. Start Strategy.*

### Test Suite G: Max Contracts Cap

*Pre-condition: Net Position is 0. Base Size is **13 contracts** ($900/$65.25).*

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
|<input type="checkbox"> | Cap clamps calculated size | 1. Place Buy Limit Qty `1` with 128-tick SL. | Order placed for Qty `5` (not 13).<br>**Log:** "Capping calculatedSize from 13 to 5". |
|<input type="checkbox"> | Micro / Mini independence | 1. Open chart for standard **NQ**.<br>2. Place Buy Limit Qty `1` with `20`-tick SL. | Order placed for Qty `8`.<br>*(Risk $900 / ($100 stop + $7.50 slip + $5 comm = $112.50 cost) = 8.0)*.<br>It is NOT capped by the Micro setting.<br>**Log:** "Changed request ... from 1 to 8".<br>*(Exact: 900/112.50 = 8.0)* |

*(Clean up: Cancel orders).*

-----

## Phase 4: Edge Cases

*Stop Strategy. Set **Max Contracts (Micro)** back to **0**. Set Risk Percent to **0.1%**. Start Strategy.*

### Test Suite M: Miscellaneous

| Done | Test Case | Mechanical Steps | Expected Result |
|------|-----------|------------------|-----------------|
|<input type="checkbox"> | Risk too big for 1 contract | 1. Place Buy Limit Qty `1` with `128`-tick SL (Budget = $4.50, Cost = $65.25). | Order flashes and cancels.<br>**Log:** "Risk too big even for 1 contract". |
|<input type="checkbox"> | Hand-check sizing math | 1. Stop Strategy. Pick a Risk Percent between `2%` and `10%`. Start Strategy.<br>2. Pick two stop losses between `50` and `200` ticks that straddle a size boundary.<br>3. Calculate expected size for each by hand.<br>4. Place both orders and verify quantities match. | Your manual calculation must match the strategy output. |

-----

## Part 2: Essential Tests

### Setup

1.  Connect to your live or prop firm connection (TakeProfitTrader, TradeDay, etc.).
2.  On prop use an **evaluation account** or **funded sim** - NOT live funds.
3.  Configure strategy with the live account selected, Risk = **2.5%**.

### Critical Path Tests (Live)

*Execute these mechanically on the live environment to verify broker API compatibility.*

| Done | Test Case | Priority | Mechanical Steps |
|------|-----------|----------|------------------|
|<input type="checkbox"> | Resizing works | P0 | Place 1 contract limit order with 128-tick SL. Verify it resizes. |
|<input type="checkbox"> | Reject mode blocks no-SL | P0 | Turn off SL. Place 1 contract limit order. Verify it instantly disappears from chart. |
|<input type="checkbox"> | Exit pass-through | P0 | Force a 1 contract market fill (NO SL, Ignore mode). Place 1 contract sell limit. Verify it does not upsize. |
|<input type="checkbox"> | Cancel/Replace flow | P1 | Place a limit order with 128-tick SL. Drag the SL to 200 ticks. Verify the original limit order cancels and a new one replaces it seamlessly. |
