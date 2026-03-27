using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public interface IPosition
    {
        IAccount Account { get; }
        ISymbol Symbol { get; }
        Side Side { get; }
        double Quantity { get; }
        double OpenPrice { get; }
    }

    public class PositionWrapper(Position position) : IPosition
    {
        public IAccount Account => new AccountWrapper(position.Account);
        public ISymbol Symbol => new SymbolWrapper(position.Symbol);

        public Side Side => position.Side;
        public double Quantity => position.Quantity;
        public double OpenPrice => position.OpenPrice;
    }

    public interface IOrder
    {
        IAccount Account { get; }
        string Id { get; }
        double Price { get; }
        double TriggerPrice { get; }
        string OrderTypeId { get; init; }
        double TotalQuantity { get; }
        OrderStatus Status { get; }
        ISymbol Symbol { get; }
        Side Side { get; }
        // ReSharper disable once UnusedMember.Global
        Order Inner { get; } // For debugging only, use wrapper in code
        TradingOperationResult Cancel();
        static IOrder Find(string orderId) =>
            Core.Instance.Orders.FirstOrDefault(o => o.Id == orderId) is { } inner
                ? new OrderWrapper(inner)
                : null;
    }

    public class OrderWrapper(Order order) : IOrder
    {
        public IAccount Account => new AccountWrapper(order.Account);
        public string Id => order.Id;
        public double Price => order.Price;
        public double TriggerPrice => order.TriggerPrice;
        public string OrderTypeId { get; init; } = order.OrderTypeId;
        public double TotalQuantity => order.TotalQuantity;
        public OrderStatus Status => order.Status;
        public ISymbol Symbol => new SymbolWrapper(order.Symbol);
        public Side Side => order.Side;
        public Order Inner => order; // For debugging only, use wrapper in code

        public TradingOperationResult Cancel() => order.Cancel();
    }

    public interface IAccount
    {
        string Id { get; }
        double Balance { get; }
        Dictionary<string, string> AdditionalInfo { get; }
    }

    public class AccountWrapper(Account account) : IAccount
    {
        public Account Inner => account;
        public string Id => account?.Id;
        public double Balance => account?.Balance ?? 0;

        public Dictionary<string, string> AdditionalInfo
        {
            get
            {
                Dictionary<string, string> additionalInfo = new();

                if (account?.AdditionalInfo?.Items == null)
                    return additionalInfo;

                foreach (var item in account.AdditionalInfo.Items)
                {
                    if (item?.Id != null)
                    {
                        additionalInfo[item.Id] = item.Value?.ToString() ?? string.Empty;
                    }
                }
                return additionalInfo;
            }
        }
    }

    public interface ISymbol
    {
        string Id { get; }
        double Last { get; }
        double TickSize { get; }
        double GetTickCost(double price);
    }

    public class SymbolWrapper(Symbol symbol) : ISymbol
    {
        public Symbol Inner => symbol;
        public string Id => symbol.Id;
        public double TickSize => symbol.TickSize;
        public double Last => symbol.Last;

        public double GetTickCost(double price) => symbol.GetTickCost(price);
    }

    // Default symbol for metrics before first order. MNQ values.
    public class DefaultSymbol : ISymbol
    {
        public string Id => "MNQ";
        public double Last => 20_000;
        public double TickSize => 0.25;

        public double GetTickCost(double price) => 0.50;
    }

    public interface IRequestParameters
    {
        long RequestId { get; }
        CancellationToken CancellationToken { get; set; }
    }

    public class RequestParametersWrapper(RequestParameters inner) : IRequestParameters
    {
        public RequestParameters BaseInner { get; } = inner;

        // Use the SDK's auto-generated or cloned ID as the source of truth
        public long RequestId => BaseInner.RequestId;

        public CancellationToken CancellationToken
        {
            get => BaseInner.CancellationToken;
            set => BaseInner.CancellationToken = value;
        }

        public static IRequestParameters Create(RequestParameters request)
        {
            return request switch
            {
                PlaceOrderRequestParameters p => new PlaceOrderRequestParametersWrapper(p),
                ModifyOrderRequestParameters m => new ModifyOrderRequestParametersWrapper(m),
                _ => new RequestParametersWrapper<RequestParameters>(request),
            };
        }
    }

    public class RequestParametersWrapper<T>(T inner) : RequestParametersWrapper(inner)
        where T : RequestParameters
    {
        public T Inner { get; } = inner;
    }

    public interface IOrderRequestParameters : IRequestParameters
    {
        double Quantity { get; set; }
        IAccount Account { get; }
        ISymbol Symbol { get; }
        double Price { get; set; }
        double TriggerPrice { get; set; }
        Side Side { get; set; }
        List<SlTpHolder> StopLossItems { get; set; }
        List<SlTpHolder> TakeProfitItems { get; set; }
        string OrderTypeId { get; init; }

        TradingOperationResult Send();
    }

    public interface IModifyOrderRequestParameters : IOrderRequestParameters
    {
        string OrderId { get; set; }
    }

    public interface IPlaceOrderRequestParameters : IOrderRequestParameters
    {
        // Used in ModifyOrderRequestParameters cancel/replace scenarios
        public static IPlaceOrderRequestParameters FromModify(
            IModifyOrderRequestParameters modify,
            double newQuantity
        )
        {
            return new PlaceOrderRequestParametersWrapper
            {
                // The replacement uses SDK's autoincrement logic for RequestId
                Account = modify.Account,
                Symbol = modify.Symbol,
                Side = modify.Side,
                Price = modify.Price,
                TriggerPrice = modify.TriggerPrice,
                OrderTypeId = modify.OrderTypeId,
                Quantity = newQuantity,
                StopLossItems = modify.StopLossItems?.ToList(), // Clone the list to avoid side effects
                TakeProfitItems = modify.TakeProfitItems?.ToList(),
            };
        }
    }

    public abstract class OrderRequestParametersWrapper<T>(T inner)
        : RequestParametersWrapper<T>(inner),
            IOrderRequestParameters
        where T : OrderRequestParameters // Constraints ensure we only wrap order-related params
    {
        // Wrap the SDK Account/Symbol with our wrappers

        public IAccount Account
        {
            get;
            init
            {
                field = value;
                // The "Unwrap" Bridge: Sync back to the SDK object
                if (value is AccountWrapper wrapper)
                {
                    Inner.Account = wrapper.Inner;
                }
            }
        } = new AccountWrapper(inner.Account);

        public ISymbol Symbol
        {
            get;
            init
            {
                field = value;
                // The "Unwrap" Bridge: Sync back to the SDK object
                if (value is SymbolWrapper wrapper)
                {
                    Inner.Symbol = wrapper.Inner;
                }
            }
        } = new SymbolWrapper(inner.Symbol);

        // SDK settable properties proxy to Inner. Read-only properties are cached.
        public double Quantity
        {
            get => Inner.Quantity;
            set
            {
                Inner.Quantity = value;
                // Simplified Sync: Just update the first SL/TP if they exist
                SyncFirstBracket(Inner.StopLossItems, value);
                SyncFirstBracket(Inner.TakeProfitItems, value);
            }
        }

        public double Price
        {
            get => Inner.Price;
            set => Inner.Price = value;
        }

        public double TriggerPrice
        {
            get => Inner.TriggerPrice;
            set => Inner.TriggerPrice = value;
        }

        public Side Side
        {
            get => Inner.Side;
            set => Inner.Side = value;
        }

        public List<SlTpHolder> StopLossItems
        {
            get => Inner.StopLossItems ?? [];
            set
            {
                var sdkList = Inner.StopLossItems;
                if (sdkList == null)
                    return;

                sdkList.Clear();
                if (value != null)
                {
                    sdkList.AddRange(value);
                    SyncFirstBracket(sdkList, this.Quantity);
                }
            }
        }

        public List<SlTpHolder> TakeProfitItems
        {
            get => Inner.TakeProfitItems ?? [];
            set
            {
                var sdkList = Inner.TakeProfitItems;
                if (sdkList == null)
                    return;

                sdkList.Clear();
                if (value != null)
                {
                    sdkList.AddRange(value);
                    SyncFirstBracket(sdkList, this.Quantity);
                }
            }
        }

        public string OrderTypeId
        {
            get => Inner.OrderTypeId;
            init => Inner.OrderTypeId = value;
        }

        public abstract TradingOperationResult Send();

        // TODO: Support multiple brackets
        private void SyncFirstBracket(List<SlTpHolder> brackets, double qty)
        {
            if (brackets is { Count: > 0 })
            {
                brackets[0].Quantity = qty;
            }
        }
    }

    public class PlaceOrderRequestParametersWrapper(PlaceOrderRequestParameters inner)
        : OrderRequestParametersWrapper<PlaceOrderRequestParameters>(inner),
            IPlaceOrderRequestParameters
    {
        public PlaceOrderRequestParametersWrapper()
            : this(new PlaceOrderRequestParameters()) { }

        public override TradingOperationResult Send() => Core.Instance.PlaceOrder(Inner);
    }

    public class ModifyOrderRequestParametersWrapper(ModifyOrderRequestParameters inner)
        : OrderRequestParametersWrapper<ModifyOrderRequestParameters>(inner),
            IModifyOrderRequestParameters
    {
        public ModifyOrderRequestParametersWrapper()
            : this(new ModifyOrderRequestParameters()) { }

        public string OrderId { get; set; } = inner.OrderId;

        public override TradingOperationResult Send() => Core.Instance.ModifyOrder(Inner);
    }
}
