using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TradingPlatform.BusinessLayer;

namespace AutoSizeStrategy
{
    public interface IOrder
    {
        string Id { get; }
        string Comment { get; }
        double TotalQuantity { get; }
        OrderStatus Status { get; }
        void Cancel();
    }

    public class OrderWrapper(Order order) : IOrder
    {
        public string Id => order.Id;
        public string Comment
        {
            get => order.Comment;
        }
        public double TotalQuantity
        {
            get => order.TotalQuantity;
        }
        public OrderStatus Status => order.Status;

        public void Cancel() => order.Cancel();
    }

    public interface IAccount
    {
        string Id { get; }
        double Balance { get; }
    }

    public class AccountWrapper(string id, double balance) : IAccount
    {
        public AccountWrapper(Account account)
            : this(id: account.Id, balance: account.Balance) { }

        public AccountWrapper(IAccount account)
            : this(id: account.Id, balance: account.Balance) { }

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

    public interface ISlTpHolder
    {
        double Price { get; }
    }

    public class SlTpHolderWrapper(SlTpHolder slTpHolder) : ISlTpHolder
    {
        public double Price => slTpHolder.Price;
    }

    public interface IRequestParameters { }

    public class RequestParametersWrapper(RequestParameters requestParameters) : IRequestParameters { }

    public interface IPlaceOrderRequestParameters : IRequestParameters
    {
        string Comment { get; set; }
        double Quantity { get; set; }
        IAccount Account { get; set; }
        ISymbol Symbol { get; set; }
        double Price { get; set; }
        List<ISlTpHolder> StopLossItems { get; set; }
        long RequestId { get; set; }
        CancellationToken CancellationToken { get; set; }
    }

    public class PlaceOrderRequestParametersWrapper(
        string comment,
        double quantity,
        IAccount account,
        ISymbol symbol,
        double price,
        List<ISlTpHolder> stopLossItems,
        long requestId = default,
        CancellationToken cancellationToken = default
    ) : IPlaceOrderRequestParameters
    {
        public PlaceOrderRequestParametersWrapper(
            PlaceOrderRequestParameters placeOrderRequestParameters
        )
            : this(
                comment: placeOrderRequestParameters.Comment,
                quantity: placeOrderRequestParameters.Quantity,
                account: new AccountWrapper(placeOrderRequestParameters.Account),
                symbol: new SymbolWrapper(placeOrderRequestParameters.Symbol),
                price: placeOrderRequestParameters.Price,
                stopLossItems:
                [
                    .. placeOrderRequestParameters
                        .StopLossItems.Select(slTpHolder => new SlTpHolderWrapper(slTpHolder))
                        .Cast<ISlTpHolder>(),
                ],
                requestId: placeOrderRequestParameters.RequestId,
                cancellationToken: placeOrderRequestParameters.CancellationToken
            ) { }

        public string Comment
        {
            get => comment;
            set => comment = value;
        }

        public double Quantity
        {
            get => quantity;
            set => quantity = value;
        }

        public IAccount Account
        {
            get => account;
            set => account = value;
        }

        public ISymbol Symbol
        {
            get => symbol;
            set => symbol = value;
        }

        public double Price
        {
            get => price;
            set => price = value;
        }

        public List<ISlTpHolder> StopLossItems
        {
            get => stopLossItems;
            set => stopLossItems = value;
        }

        public long RequestId
        {
            get => requestId;
            set => requestId = value;
        }

        public CancellationToken CancellationToken
        {
            get => cancellationToken;
            set => cancellationToken = value;
        }
    }
}
