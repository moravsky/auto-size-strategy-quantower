# AutoSizeStrategy Manual E2E Testing Guide

For minor releases do Part 1 on a replay connection and Part 2 on a live connection. For major releases do all testing on a live connection.

## Prerequisites

1. Build the strategy in Release mode
2. Ensure DLL is copied to `C:\Quantower\Settings\Scripts\Strategies\AutoSizeStrategy`
3. Have a chart with NQ/MNQ or similar futures symbol ready

---

## Part 1: Replay Connection Testing

### Setup

1. Launch Quantower
2. Connect to **Connections → Replay** (use any historical data source)
3. Set stating account balance to 150000
4. Open a chart for MNQ
5. Go to **Strategies Manager → AutoSizeStrategy42**
6. Configure:
   - Target Account: Select the Replay account
   - Risk Percent: 2.5%
   - Missing Stop Loss Action: **Reject**
   - Minimum Account Balance: 145500
   - Clutch Mode Budget: 1350
7. Start the strategy

---

### Test Suite A: Basic Order Resizing

| Done | # | Test Case | Steps | Expected Result |
|------|---|-----------|-------|-----------------|
| <input type="checkbox"> | A1 | Limit order with SL resizes | Place limit order (qty=1) with 20-tick SL via DOM | Order quantity changes to calculated size based on selected risk |
| <input type="checkbox"> | A2 | Market order with SL resizes | Place market order (qty=1) with 20-tick SL | Order fills at calculated size |
| <input type="checkbox"> | A3 | Stop order with SL resizes | Place stop order (qty=1) with 20-tick SL | Order placed at calculated size |
| <input type="checkbox"> | A4 | Large SL = smaller size | Place order with 100-tick SL | Size smaller than A1 (more risk per contract) |
| <input type="checkbox"> | A5 | Tiny SL = larger size | Place order with 5-tick SL | Size larger than A1 (less risk per contract) |

**Verification**: Check strategy logs (left-click strategy → Message) for "Changed request X quantity from Y to Z"

---

### Test Suite B: Stop Loss Enforcement

| Done | # | Test Case | Steps | Expected Result |
|------|---|-----------|-------|-----------------|
| <input type="checkbox"> | B1 | No SL + Reject mode | With "Reject" mode, place order without SL | Order cancelled (qty=0), log shows "cancelled: stop loss required" |
| <input type="checkbox"> | B2 | No SL + Ignore mode | Stop strategy, change to "Ignore", restart. Place order without SL | Order passes through unchanged |
| <input type="checkbox"> | B3 | SL added after order | Place limit order without SL in "Ignore" mode, then add SL via modify | If modifying adds SL, should now process correctly |

---

### Test Suite C: Exit Order Pass-Through

| Done | # | Test Case | Steps | Expected Result |
|------|---|-----------|-------|-----------------|
| <input type="checkbox"> | C1 | Exit order with SL unchanged | Enter long position (2 contracts). Place sell order for 1 contract | Sell order passes through at qty=1, not resized. Log: "Passing through exit request" |
| <input type="checkbox"> | C2 | Exit order without SL unchanged | With position open, place exit order without SL | Passes through (exits exempt from SL requirement) |

---

### Test Suite D: Order Modification (Cancel/Replace)

| Done | # | Test Case | Steps | Expected Result |
|------|---|-----------|-------|-----------------|
| <input type="checkbox"> | D1 | Modify order SL changes size | Place limit order, then modify SL from 20 to 40 ticks | Original order cancelled, new order placed with recalculated size. Log: "resizing order via Cancel/Replace" |
| <input type="checkbox"> | D2 | Modify order price only | Modify price without changing SL | Order modified normally (no cancel/replace needed if qty unchanged) |

---

### Test Suite E: Account Longevity Metrics

| Done | # | Test Case | Steps | Expected Result |
|------|---|-----------|-------|-----------------|
| <input type="checkbox"> | E1 | Clutch Mode Budget not set | Set Clutch Mode Budget to 0. Start the strategy | Trades to clutch/bust displayed as "N/A" |
| <input type="checkbox"> | E2 | Regular Mode | Set risk 8%, balance to 150K, min balance override to 145500. Start the strategy | Drawdown: 4500, Trades to clutch: 14, Trades to bust: 17 |
| <input type="checkbox"> | E3 | Metrics Update | Set risk 8%, balance to 150K, min balance override to 145500. Start the strategy. Take a significant trade | Metrics update with a win/loss |
| <input type="checkbox"> | E4 | Clutch Mode | Set balance to 146400, min balance override to 145500. Start the strategy | Drawdown: 400, Trades to clutch: 0, Trades to bust: 2 |

### Test Suite F: Position-Aware Sizing

| Done | # | Test Case | Steps | Expected Result |
|------|---|-----------|-------|-----------------|
| <input type="checkbox"> | F1 | Existing position reduces allowed increment | Enter long via market order for qty=3 (strategy will resize — note actual fill qty). Place another buy limit order for qty=1000 | Second order resized to calculatedSize minus current position. Log shows "Changed request X quantity from 1000 to Y" |
| <input type="checkbox"> | F2 | Pyramiding blocked at target | Enter long at full calculatedSize via market order. Place another buy order | Order cancelled (remaining capacity=0). Log shows "position already at target size" |
| <input type="checkbox"> | F3 | Short position allows top-up | Enter short via market order (strategy sizes it). Manually close half via buy. Place another sell order for qty=1000 | Sell order resized to remaining short capacity. Log shows resize |
| <input type="checkbox"> | F4 | Position Flip / Reversal | Enter long via market order for qty=2. Place a sell order for qty=1000 with a SL. | Order is resized to (2 + calculatedSize). Log shows resize. Position successfully flips to short at target capacity. |

### Test Suite G: Max Contracts Cap

Setup: Risk Percent 10%, balance $150K static account (uncapped calculatedSize = 150 @ 20-tick stop).

| Done | # | Test Case | Steps | Expected Result |
|------|---|-----------|-------|-----------------|
| <input type="checkbox"> | G1 | Cap below calculated size | Set Max Contracts (Micro) to 50. Place MNQ order qty=1 with 20-tick SL | Order capped at 50. Log shows "Capping calculatedSize from 150 to 50". |
| <input type="checkbox"> | G2 | Micro and mini caps are independent | Set Max Contracts (Micro)=50, Max Contracts (Mini)=5. Place NQ order, then MNQ order | NQ capped at 5, MNQ capped at 50. Each uses its own setting. |

### Test Suite H: Value at Risk Reporting

| Done | # | Test Case | Steps | Expected Result |
|------|---|-----------|-------|-----------------|
| <input type="checkbox"> | V1 | Protected Position | Enter 1 contract limit order with exactly 20 tick SL. Let it fill. | Absolute Value at Risk reads exact dollar risk based on OpenPrice distance to SL plus slippage. Relative Value at Risk calculates against distance-to-liquidation. |
| <input type="checkbox"> | V2 | StopLimit evaluation | Enter 1 contract position. Place a StopLimit SL where Trigger and Limit are 5 ticks apart. | Absolute Value at Risk uses the Limit Price distance, not the Trigger price distance. |
| <input type="checkbox"> | V3 | Modified SL changes Risk | With position open, drag the SL on chart further away. | Within 1 second (Heartbeat), Absolute Value at Risk and Relative update to new higher values. |
| <input type="checkbox"> | V4 | Unprotected Position Spike | Enter a limit order with NO stop loss (Ignore Mode). Let it fill. | Absolute Value at Risk spikes to distance-to-liquidation (DTF). Relative Value at Risk shows 100%. |
| <input type="checkbox"> | V5 | Static Account Unprotected | On a Sim/Static account with no AutoLiquidateThreshold, enter Unprotected position. | Absolute Value at Risk shows full Account Balance. |


### Test Suite M: Miscellaneous

| Done | # | Test Case | Steps | Expected Result |
|------|---|-----------|-------|-----------------|
| <input type="checkbox"> | M1 | Risk too big for 1 contract | Set Risk Percent to 0.1%. Place order with 50-tick SL | Order cancelled (qty=0). Log: "Risk too big even for 1 contract" |
| <input type="checkbox"> | M2 | Modify latency acceptable | Place limit order, then modify SL | Modification completes within ~1 second (no noticeable lag) |

---

## Part 2: Live Connection Testing

### Setup

1. Connect to your prop firm connection (TakeProfitTrader, TradeDay, etc.)
2. Use an **evaluation account** or **funded sim** - NOT live
3. Configure strategy with appropriate account selected
4. Configure small risk percentage (2.5%)

---

### Critical Path Tests (Live)

| Done | # | Test Case | Priority | Notes |
|------|---|-----------|----------|-------|
| <input type="checkbox"> | L1 | Order resizing works | P0 | Place 1 contract order with 20-tick SL, verify resize |
| <input type="checkbox"> | L2 | Reject mode blocks no-SL | P0 | Attempt order without SL, verify rejection |
| <input type="checkbox"> | L3 | Exit pass-through | P0 | Enter position, verify exit orders aren't resized |
| <input type="checkbox"> | L4 | Cancel/Replace flow | P1 | Modify an order's SL, verify clean cancel/replace |
| <input type="checkbox"> | L5 | Reconnection | P2 | Cycle connection, verify strategy works without restart |

---

### Account-Specific Drawdown Tests

| Account Pattern | Expected Mode | Verification |
|-----------------|---------------|--------------|
| `TPPRO123456` | Intraday | Uses `AutoLiquidateThresholdCurrentValue` from account info |
| `TPT987654` | EndOfDay | Uses `Minimum Account Balance` from strategy settings |
| Other | Static | Uses 10% of total balance |

**How to verify**: Check strategy logs for "Changed request X quantity from Y to Z" — correct sizing confirms the drawdown mode is working

---

## Quick Smoke Test Checklist
<input type="checkbox"> Strategy running  
<input type="checkbox"> Correct account selected  
<input type="checkbox"> Correct risk percent selected  
<input type="checkbox"> Correct Missing Stop Loss Action selected  
<input type="checkbox"> Correct SL tick size selected  
<input type="checkbox"> Place test order with SL → verify resize in logs  
<input type="checkbox"> Modify test order with SL → verify resize in logs  
<input type="checkbox"> Place test order without SL → verify rejection  
<input type="checkbox"> Cancel test order  

---

## Debugging Tips

1. **View Logs**: Left-click strategy in Strategies Manager or go to C:\Quantower\Settings\Scripts\ScriptsData\{StrategyName} ({InstanceId})\logs\YYYYMMDD.slog
2. **Attach Debugger**: Use VSCodium launch config to attach to Quantower
3. **Check Account Info**: If drawdown calculations seem wrong, look at Account Info panel or dump Account.AddtionalInfo
4. **Replay Speed**: Slow down replay to 1x when debugging timing issues

---

## Known Edge Cases to Watch

1. **Rapid Order Flow**: Strategy should handle 30-50 orders/day without issues
2. **Connection Drops**: Strategy should survive reconnection gracefully