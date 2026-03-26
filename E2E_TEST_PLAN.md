### AutoSizeStrategy Manual E2E Testing Guide

For major releases do all Part 1 tests in Replay environment and then on live connection.
For minor releases do all Part 1 tests in Replay environment and then Part 2 tests on live connection.
Replay and live connections behvior has subtle differences.

## Prerequisites

1.  Build the strategy in Release mode.
2.  Ensure DLL is copied to `C:\Quantower\Settings\Scripts\Strategies\AutoSizeStrategy`.
3.  Have a chart open for **MNQ**.

-----

## Part 1: Extensive Test Suite

### Baseline Configuration

*The math for the following tests relies strictly on this configuration.*

1.  Connect to **Connections → Replay** (use any historical data source).
2.  Set the starting account balance to **$150,000**.
3.  Go to **Strategies Manager → AutoSizeStrategy42**.
4.  Configure the settings as follows:
      * **Target Account:** Select the Replay/Sim account
      * **Drawdown Mode:** End of Day *(Forces deterministic calculation)*
      * **Minimum Balance Override:** 145500 *(Risk Capital = $4,500)*
      * **Risk Percent:** 2.5% *(Risk Budget = $112.50)*
      * **Action on Missing Stop Loss:** Reject
      * **Initial Stop Loss Ticks:** 64
      * **Average Slippage Ticks:** 1.0
      * **Commission Micro:** 0.25
      * **Max Contracts (Micro):** 0 *(Disabled)*
      * **Clutch Mode Budget:** 1350

*Base Math Reference:*

  * MNQ Tick Value = $0.50
  * 64 Ticks = $32.00 Risk per contract
  * Risk Budget ($112.50) / $32.00 = 3.51
  * **Base Target Size = 3 contracts**

-----

### Test Suite A: Basic Order Resizing

*Pre-condition: Ensure Net Position is 0.*

| Done | \# | Test Case | Mechanical Steps | Expected Result |
|------|---|-----------|------------------|-----------------|
|<input type="checkbox"> | A1 | Limit order with standard SL | 1. Open MNQ chart.<br>2. Set Order Qty to `1`.<br>3. Enable SL and set to `64` ticks.<br>4. Click **Buy Bid**. | Order appears on the chart with Qty `3`.<br>**Log:** "Changed request ... quantity from 1 to 3." |
|<input type="checkbox"> | A2 | Market order with standard SL | 1. Set Order Qty to `1`.<br>2. Enable SL to `64` ticks.<br>3. Click **Buy Market**. | Position opens with Qty `3`. |
|<input type="checkbox"> | A3 | Stop order with standard SL | 1. Set Order Qty to `1`.<br>2. Enable SL to `64` ticks.<br>3. Place a Buy Stop. | Pending Stop Order appears with Qty `3`. |
|<input type="checkbox"> | A4 | Large SL = Smaller Size | 1. Set Order Qty to `1`.<br>2. Change SL to `100` ticks.<br>3. Place a Buy Limit order. | Order Qty is `2`. *(112.50 / $50 risk = 2.25)*<br>**Log:** "quantity from 1 to 2." |
|<input type="checkbox"> | A5 | Tight SL = Larger Size | 1. Set Order Qty to `1`.<br>2. Change SL to `10` ticks.<br>3. Place a Buy Limit order. | Order Qty is `22`. *(112.50 / $5 risk = 22.5)*<br>**Log:** "quantity from 1 to 22." |

*(Clean up: Cancel all working orders and flatten positions before continuing).*

-----

### Test Suite B: Stop Loss Enforcement

*Pre-condition: Net Position is 0. Strategy setting `Action on Missing Stop Loss` = Reject.*

| Done | \# | Test Case | Mechanical Steps | Expected Result |
|------|---|-----------|------------------|-----------------|
|<input type="checkbox"> | B1 | Reject Mode enforcement | 1. Disable SL entirely.<br>2. Place a Buy Limit order for Qty `1`. | Order flashes and disappears.<br>**Log:** "cancelled: stop loss required". |
|<input type="checkbox"> | B2 | Ignore Mode enforcement | 1. Stop Strategy.<br>2. Change action to **Ignore**.<br>3. Start Strategy.<br>4. Place a Buy Limit order for Qty `1` with NO SL. | Order is successfully placed with Qty `1`.<br>**Log:** "has no stop loss - passing through unchanged". |
|<input type="checkbox"> | B3 | Cancel/Replace when SL added | 1. With the Qty `1` order from B2 resting, right-click it on the chart → **Modify**.<br>2. Check "Stop Loss", set offset to `64` ticks.<br>3. Click **Apply**. | Original order cancels. New Limit order appears with Qty `3` and a 64-tick SL.<br>**Log:** "resizing order ... via Cancel/Replace". |

*(Clean up: Cancel all working orders and flatten positions. Stop Strategy, set `Action on Missing Stop Loss` back to **Reject**, Restart Strategy).*

-----

### Test Suite C: Exit Order Pass-Through

*Pre-condition: Enter a Long position of 3 contracts (e.g., Buy Market Qty 1 with 64-tick SL).*

| Done | \# | Test Case | Mechanical Steps | Expected Result |
|------|---|-----------|------------------|-----------------|
|<input type="checkbox"> | C1 | Exit with SL is ignored | 1. Enable SL to `64` ticks.<br>2. Place a Sell Limit order above market for Qty `2`. | Sell Limit is placed for Qty `2` (does not upsize to 3).<br>**Log:** "Passing through exit request". |
|<input type="checkbox"> | C2 | Exit without SL is ignored | 1. Disable SL.<br>2. Place a Sell Limit order above market for Qty `1`. | Sell Limit is placed for Qty `1`.<br>**Log:** "Passing through exit request". |

*(Clean up: Flatten positions).*

-----

### Test Suite D: Order Modification

*Pre-condition: Net Position is 0. SL enabled to 64 ticks.*

| Done | \# | Test Case | Mechanical Steps | Expected Result |
|------|---|-----------|------------------|-----------------|
|<input type="checkbox"> | D1 | Expanding SL shrinks size | 1. Place Buy Limit Qty `1` (Sizes to `3`).<br>2. Right-click the order → **Modify**.<br>3. Change SL offset to `100` ticks.<br>4. Click **Apply**. | Order cancels and replaces with Qty `2`. *(112.50 / 50 = 2)*<br>**Log:** "resizing order ... via Cancel/Replace". |
|<input type="checkbox"> | D2 | Moving Price only | 1. Left-click and drag the resting Buy Limit order down by 5 points on the chart.<br>2. Release mouse. | Order moves to new price. Qty remains `2`. Stop loss moves down with it to maintain 100-tick distance. |

*(Clean up: Cancel working orders).*

-----

### Test Suite F: Position-Aware Addition & Reversals

*Pre-condition: Stop Strategy. Set Risk Percent to **5.0%** (Budget = $225). Base Capacity is now **7 contracts** ($225 / $32). Start Strategy.*

| Done | \# | Test Case | Mechanical Steps | Expected Result |
|------|---|-----------|------------------|-----------------|
|<input type="checkbox"> | F1 | Existing position reduces capacity | 1. Place Buy Market Qty `1` with 64-tick SL (Fills `7` contracts).<br>2. Close `4` contracts manually, leaving Net Position at Long `3`.<br>3. Place a new Buy Limit order Qty `1` with 64-tick SL. | Order is placed with Qty `4`. *(Capacity 7 - Existing 3 = 4)*.<br>**Log:** "Changed request ... from 1 to 4". |
|<input type="checkbox"> | F2 | Hard stop at max capacity | 1. Flatten position and cancel any working orders.<br>2. Click **MKT** (Buy) Qty `1` with 64-tick SL (Fills `7` contracts).<br>3. Place a new Buy Limit order Qty `1` with 64-tick SL. | The second order flashes and is immediately cancelled. Qty = 0.<br>**Log:** "position already at target size (7)". |
|<input type="checkbox"> |F3 | Position Flip / Reversal | 1. Ensure Net Position is Long 7. 2. Set Order Quantity to 100. 3. Click MKT (Sell) with 64-tick SL. | Observe: Sell Market order is safely clamped/sized to 14 contracts (7 to flatten + 7 max new short exposure). Net position instantly becomes Short 7. |

*(Clean up: Flatten positions).*

-----

### Test Suite G: Max Contracts Cap

*Pre-condition: Net Position is 0. Stop Strategy. Set Risk Percent to **10.0%** (Base Size is **14 contracts**). Set **Max Contracts (Micro)** to **5**. Start Strategy.*

| Done | \# | Test Case | Mechanical Steps | Expected Result |
|------|---|-----------|------------------|-----------------|
|<input type="checkbox"> | G1 | Cap clamps calculated size | 1. Place Buy Limit Qty `1` with 64-tick SL. | Order placed for Qty `5` (not 14).<br>**Log:** "Capping calculatedSize from 14 to 5". |
|<input type="checkbox"> | G2 | Micro / Mini independence | 1. Open chart for standard **NQ**.<br>2. Place Buy Limit Qty `1` with `20`-tick SL. | Order placed for Qty `4`. *(Risk $450 / ($5 \* 20 ticks = $100) = 4)*. It is NOT capped by the Micro setting.<br>**Log:** "Changed request ... from 1 to 4". |

*(Clean up: Cancel orders, reset Max Contracts Micro to 0, reset Risk to 2.5%).*

-----

### Test Suite H: Value at Risk (VaR) Reporting
*Pre-condition: Strategy running. Risk Percent 2.5%. Balance $150,000. Override 145500. Expected Max Exposure = $4500.*

| Done | # | Test Case | Mechanical Steps | Expected Result |
|------|---|-----------|------------------|-----------------|
| <input type="checkbox"> | H1 | Single Protected Position | 1. Place Buy Limit Qty `1` with 64-tick SL (Sizes to `3`).<br>2. Allow order to fill.<br>3. Open Strategy Metrics panel. | **Observe Absolute VaR:** Exactly `$49.50`. *(3 qty * 64 ticks * $0.50 tick value = $48.00 + $1.50 slippage)*.<br>**Observe Relative VaR:** Exactly `1.10%`. *(49.50 / 4500)* |
| <input type="checkbox"> | H2 | Modified SL updates VaR | 1. Drag the resting SL line on the chart down from 64 ticks to 100 ticks. | **Observe Absolute VaR:** Jumps to `$76.50` within 1 second. *(3 * 100 * 0.50 + 1.50 slippage)*. |
| <input type="checkbox"> | H3 | Partial Protection Spike | 1. Right-click the resting SL line on the chart → **Modify**.<br>2. Change the order Quantity from `3` to `2` and click **Apply**.<br>*(This leaves 1 position contract completely unprotected).* | **Observe Absolute VaR:** Spikes instantly to `$4500.00` (Max Exposure).<br>**Observe Relative VaR:** Spikes to `100%`. |
| <input type="checkbox"> | H4 | Multiple Stops (Blended) | 1. With the position of `3` still open, right-click the chart exactly 64 ticks (16 points) below the entry price.<br>2. Place a **Sell Stop** order for Qty `1`.<br>*(You now have 2 contracts protected at 100 ticks, and 1 contract protected at 64 ticks).* | **Observe Absolute VaR:** Drops to exactly `$117.50`.<br>*(Qty 2 @ 100 ticks = $51.00)*<br>*(Qty 1 @ 64 ticks = $16.50)*<br>**Observe Relative VaR:** Exactly `1.50%`. *(67.50 / 4500)* |
| <input type="checkbox"> | H5 | Unprotected Spike | 1. Flatten position.<br>2. Stop Strategy, set Missing Stop Loss Action to **Ignore**. Start Strategy.<br>3. Click **NO SL** in Order Entry.<br>4. Click **MKT** (Buy) Qty `1`. | **Observe Absolute VaR:** Spikes instantly to `$4500.00` (Max Exposure).<br>**Observe Relative VaR:** Spikes to `100%`. |

*(Clean up: Flatten positions. Set Missing Stop Loss Action back to Reject).*

-----

### Test Suite M: Miscellaneous Edge Cases

| Done | \# | Test Case | Mechanical Steps | Expected Result |
|------|---|-----------|------------------|-----------------|
|<input type="checkbox"> | M1 | Risk too big for 1 contract | 1. Change Risk Percent to `0.1%` (Budget = $4.50).<br>2. Place Buy Limit Qty `1` with `64`-tick SL (Risk = $32). | Order flashes and cancels.<br>**Log:** "Risk too big even for 1 contract". |

-----

## Part 2: Essential Tests

### Setup

1.  Connect to your live or prop firm connection (TakeProfitTrader, TradeDay, etc.).
2.  On prop use an **evaluation account** or **funded sim** - NOT live funds.
3.  Configure strategy with the live account selected, Risk = **2.5%**.

### Critical Path Tests (Live)

*Execute these mechanically on the live environment to verify broker API compatibility.*

| Done | \# | Test Case | Priority | Mechanical Steps |
|------|---|-----------|----------|------------------|
|<input type="checkbox"> | L1 | Resizing works | P0 | Place 1 contract limit order with 64-tick SL. Verify it resizes to 3. |
|<input type="checkbox"> | L2 | Reject mode blocks no-SL | P0 | Turn off SL. Place 1 contract limit order. Verify it instantly disappears from chart. |
|<input type="checkbox"> | L3 | Exit pass-through | P0 | Force a 1 contract market fill (NO SL, Ignore mode). Place 1 contract sell limit. Verify it does not upsize. |
|<input type="checkbox"> | L4 | Cancel/Replace flow | P1 | Place a limit order with 64-tick SL. Drag the SL to 100 ticks. Verify the original limit order cancels and a new one replaces it seamlessly. |