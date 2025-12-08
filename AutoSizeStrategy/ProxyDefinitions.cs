using System.Linq;
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
        double Balance { get; }
    }

    public class AccountWrapper(Account realAccount) : IAccount
    {
        public double Balance => realAccount.Balance;
    }
}
