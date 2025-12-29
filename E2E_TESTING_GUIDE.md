# AutoSizeStrategy Manual E2E Testing Guide

## Prerequisites

1. Build the strategy in Debug or Release mode
2. Ensure DLL is copied to `C:\Quantower\Settings\Scripts\Strategies\AutoSizeStrategy`
3. Have a chart with NQ/MNQ or similar futures symbol ready

---

## Part 1: Replay Connection Testing

### Setup

1. Launch Quantower
2. Connect to **Connections → Replay** (use any historical data source)
3. Open a chart for MNQ or NQ
4. Go to **Strategies Manager → AutoSizeStrategy42**
5. Configure:
   - Target Account: Select the Replay account
   - Risk Percent: 10%
   - Missing Stop Loss Action: **Reject**
6. Start the strategy

---

### Test Suite A: Basic Order Resizing

| # | Test Case | Steps | Expected Result |
|---|-----------|-------|-----------------|
| A1 | Limit order with SL resizes | Place limit order (qty=1) with 20-tick SL via DOM | Order quantity changes to calculated size based on 10% risk |
| A2 | Market order with SL resizes | Place market order (qty=1) with 20-tick SL | Order fills at calculated size |
| A3 | Stop order with SL resizes | Place stop order (qty=1) with 20-tick SL | Order placed at calculated size |
| A4 | Large SL = smaller size | Place order with 100-tick SL | Size smaller than A1 (more risk per contract) |
| A5 | Tiny SL = larger size | Place order with 5-tick SL | Size larger than A1 (less risk per contract) |

**Verification**: Check strategy logs (left-click strategy → Message) for "Changed request X quantity from Y to Z"

---

### Test Suite B: Stop Loss Enforcement

| # | Test Case | Steps | Expected Result |
|---|-----------|-------|-----------------|
| B1 | No SL + Reject mode | With "Reject" mode, place order without SL | Order cancelled (qty=0), log shows "cancelled: stop loss required" |
| B2 | No SL + Ignore mode | Stop strategy, change to "Ignore", restart. Place order without SL | Order passes through unchanged |
| B3 | SL added after order | Place limit order without SL in "Ignore" mode, then add SL via modify | If modifying adds SL, should now process correctly |

---

### Test Suite C: Exit Order Pass-Through

| # | Test Case | Steps | Expected Result |
|---|-----------|-------|-----------------|
| C1 | Exit order unchanged | Enter long position (2 contracts). Place sell order for 1 contract | Sell order passes through at qty=1, not resized. Log: "Passing through exit request" |
| C2 | Full exit unchanged | With 2-contract long, sell 2 contracts | Passes through unchanged |
| C3 | Exit without SL | With position open, place exit order without SL | Passes through (exits exempt from SL requirement) |

---

### Test Suite D: Order Modification (Cancel/Replace)

| # | Test Case | Steps | Expected Result |
|---|-----------|-------|-----------------|
| D1 | Modify order SL changes size | Place limit order, then modify SL from 20 to 40 ticks | Original order cancelled, new order placed with recalculated size. Log: "resizing order via Cancel/Replace" |
| D2 | Modify order price only | Modify price without changing SL | Order modified normally (no cancel/replace needed if qty unchanged) |

---

### Test Suite E: Miscellaneous

| # | Test Case | Steps | Expected Result |
|---|-----------|-------|-----------------|
| E1 | Risk too big for 1 contract | Set Risk Percent to 0.1%. Place order with 50-tick SL | Order cancelled (qty=0). Log: "Risk too big even for 1 contract" |
| E2 | Modify latency acceptable | Place limit order, then modify SL | Modification completes within ~1 second (no noticeable lag) |

---

## Part 2: Live Connection Testing

### Setup

1. Connect to your prop firm connection (TakeProfitTrader, TradeDay, etc.)
2. Use an **evaluation account** or **funded sim** - NOT live funded
3. Configure strategy with appropriate account selected
4. Configure small risk percentage (2.5%)

---

### Critical Path Tests (Live)

| # | Test Case | Priority | Notes |
|---|-----------|----------|-------|
| L1 | Order resizing works | P0 | Place 1 contract order with 20-tick SL, verify resize |
| L2 | Reject mode blocks no-SL | P0 | Attempt order without SL, verify rejection |
| L3 | Exit pass-through | P0 | Enter position, verify exit orders aren't resized |
| L4 | Cancel/Replace flow | P1 | Modify an order's SL, verify clean cancel/replace |
| L5 | Reconnection | P2 | Cycle connection, verify strategy works without restart |

---

### Account-Specific Drawdown Tests

| Account Pattern | Expected Mode | Verification |
|-----------------|---------------|--------------|
| `TPPRO123456` | Intraday | Uses `AutoLiquidateThresholdCurrentValue` from account info |
| `TPT987654` | EndOfDay | Uses `AutoLiquidateThreshold` + `MinAccountBalance` + `NetPnL` |
| Other | Static | Uses 10% of total balance |

**How to verify**: Check strategy logs for "Changed request X quantity from Y to Z" — correct sizing confirms the drawdown mode is working

---

## Quick Smoke Test Checklist
- Strategy running
- Correct account selected
- Correct risk percent selected
- Correct SL tick size selected
- Correct Missing Stop Loss Action selected
- Place test order with SL → verify resize in logs
- Modify test order with SL → verify resize in logs
- Place test order without SL → verify rejection
- Cancel test order

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
