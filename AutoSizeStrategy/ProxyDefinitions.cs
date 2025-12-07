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

    // 2. Your Wrapper (Simple, no compilation errors, no strictness fighting)
    public class OrderWrapper(Order order) : IOrder
    {
        //private readonly Order _order = order;

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
}
