using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    }

    public class PositionWrapper(Position position) : IPosition
    {
        public IAccount Account => new AccountWrapper(position.Account);
        public ISymbol Symbol => new SymbolWrapper(position.Symbol);

        public Side Side => position.Side;
        public double Quantity => position.Quantity;
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
        SlTpHolder[] StopLossItems { get; }
        ISymbol Symbol { get; }
        Side Side { get; }
        TradingOperationResult Cancel();
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

        public SlTpHolder[] StopLossItems => order.StopLossItems ?? [];

        public ISymbol Symbol => new SymbolWrapper(order.Symbol);

        public Side Side => order.Side;

        public TradingOperationResult Cancel() => order.Cancel();
    }

    public interface IAccount
    {
        string Id { get; }
        double Balance { get; }
        Dictionary<string, string> AdditionalInfo { get; }
        string DumpAdditionalInfo();
    }

    public class AccountWrapper(Account account) : IAccount
    {
        public AccountWrapper(IAccount wrapper)
            : this((Account)null)
        {
            Id = wrapper?.Id ?? default;
            Balance = wrapper?.Balance ?? default;
        }

        public string Id { get; } = account?.Id ?? default;
        public double Balance { get; } = account?.Balance ?? default;

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
                        // Safe string conversion
                        additionalInfo[item.Id] = item.Value?.ToString() ?? string.Empty;
                    }
                }
                return additionalInfo;
            }
        }

        public string DumpAdditionalInfo()
        {
            if (account?.AdditionalInfo == null)
                return "No AdditionalInfo";

            var sb = new StringBuilder();
            foreach (var item in account.AdditionalInfo.Items)
                sb.AppendLine($"{item?.Id}: {item?.Value}");
            return sb.ToString();
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
        public string Id => symbol.Id;
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
        Side Side { get; set; }
        List<SlTpHolder> StopLossItems { get; set; }
        string OrderTypeId { get; init; }
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

        public Side Side
        {
            get => Inner.Side;
            set => Inner.Side = value;
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

        public string OrderTypeId { get; init; } = inner.OrderTypeId;
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
