using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public interface IOrder
    {
        IAccount Account { get; }
        string Id { get; }
        double Price { get; }
        double TotalQuantity { get; }
        OrderStatus Status { get; }
        SlTpHolder[] StopLossItems { get; }
        ISymbol Symbol { get; }
        TradingOperationResult Cancel();
    }

    public class OrderWrapper(Order order) : IOrder
    {
        public IAccount Account => new AccountWrapper(order.Account);

        public string Id => order.Id;

        public double Price => order.Price;

        public double TotalQuantity => order.TotalQuantity;

        public OrderStatus Status => order.Status;

        public SlTpHolder[] StopLossItems => order.StopLossItems ?? [];

        public ISymbol Symbol => new SymbolWrapper(order.Symbol);

        public TradingOperationResult Cancel() => order.Cancel();
    }

    public interface IAccount
    {
        string Id { get; }
        double Balance { get; }
    }

    public class AccountWrapper(string id, double balance) : IAccount
    {
        public AccountWrapper(Account account)
            : this(id: account?.Id ?? default, balance: account?.Balance ?? default) { }

        public AccountWrapper(IAccount account)
            : this(id: account?.Id ?? default, balance: account?.Balance ?? default) { }

        public string Id => id;
        public double Balance => balance;
    }

    public interface ISymbol
    {
        double Last { get; }
        double TickSize { get; }
        double GetTickCost(double price);
    }

    public class SymbolWrapper(Symbol symbol) : ISymbol
    {
        public double TickSize => symbol.TickSize;
        public double Last => symbol.Last;

        public double GetTickCost(double price) => symbol.GetTickCost(price);
    }

    public interface IRequestParameters
    {
        long RequestId { get; set; }
        CancellationToken CancellationToken { get; set; }
    }

    public class RequestParametersWrapper(RequestParameters inner) : IRequestParameters
    {
        private long _requestId;

        public RequestParameters BaseInner { get; } = inner;

        public long RequestId
        {
            get => BaseInner?.RequestId ?? _requestId;
            set => _requestId = value;
        }

        public CancellationToken CancellationToken
        {
            get => BaseInner?.CancellationToken ?? default;
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
        List<SlTpHolder> StopLossItems { get; set; }
    }

    public interface IModifyOrderRequestParameters : IOrderRequestParameters { }

    public interface IPlaceOrderRequestParameters : IOrderRequestParameters { }

    public abstract class OrderRequestParametersWrapper(OrderRequestParameters inner)
        : RequestParametersWrapper<OrderRequestParameters>(inner),
            IOrderRequestParameters
    {
        public double Quantity
        {
            get => Inner?.Quantity ?? default;
            set => Inner.Quantity = value;
        }

        public IAccount Account { get; init; } = new AccountWrapper(inner.Account);

        public ISymbol Symbol { get; init; } = new SymbolWrapper(inner.Symbol);

        public double Price
        {
            get => Inner.Price;
            set => Inner.Price = value;
        }

        public List<SlTpHolder> StopLossItems
        {
            // proxy to inner
            get => Inner.StopLossItems ?? [];
            set
            {
                // If the SDK list is null, we return safely to avoid the crash.
                var sdkList = Inner.StopLossItems;
                if (sdkList == null)
                    return;

                sdkList.Clear();
                if (value != null)
                {
                    sdkList.AddRange(value);
                }
            }
        }
    }

    public class PlaceOrderRequestParametersWrapper(PlaceOrderRequestParameters inner)
        : OrderRequestParametersWrapper(inner),
            IPlaceOrderRequestParameters
    {
        // Secondary constructor for tests/mocking
        public PlaceOrderRequestParametersWrapper()
            : this(new PlaceOrderRequestParameters()) { }
    }

    public class ModifyOrderRequestParametersWrapper(ModifyOrderRequestParameters inner)
        : OrderRequestParametersWrapper(inner),
            IModifyOrderRequestParameters
    {
        // Secondary constructor for tests/mocking
        public ModifyOrderRequestParametersWrapper()
            : this(new ModifyOrderRequestParameters()) { }
    }
}
