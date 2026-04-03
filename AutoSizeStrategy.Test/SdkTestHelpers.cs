using System.Runtime.CompilerServices;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy.Test
{
    public static class SdkTestHelpers
    {
        public static Account CreateFakeAccount(string? id = null, double balance = 0)
        {
            var account = (Account)RuntimeHelpers.GetUninitializedObject(typeof(Account));

            if (id != null)
                typeof(Account).GetProperty("Id")!.SetValue(account, id);

            typeof(Account).GetProperty("Balance")!.SetValue(account, balance);

            return account;
        }

        public static Symbol CreateFakeSymbol(
            string? id = null,
            string? name = null,
            double tickSize = 0.25,
            double tickCost = 1.0,
            double last = 0
        )
        {
            var symbol = (Symbol)RuntimeHelpers.GetUninitializedObject(typeof(Symbol));

            if (id != null)
                typeof(Symbol).GetProperty("Id")!.SetValue(symbol, id);

            if (name != null)
                typeof(Symbol).GetProperty("Name")!.SetValue(symbol, name);

            typeof(Symbol).GetProperty("Last")!.SetValue(symbol, last);

            // VariableTickList setter caches TickSize when list has exactly 1 element.
            // VariableTick(tickSize, tickCost) covers all prices (-Inf to +Inf).
            var variableTicks = new List<VariableTick> { new(tickSize, tickCost) };
            typeof(Symbol)
                .GetProperty("VariableTickList")!
                .SetValue(symbol, variableTicks);

            return symbol;
        }
    }
}
