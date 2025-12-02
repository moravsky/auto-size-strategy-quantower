using System.Linq;
using TradingPlatform.BusinessLayer;

public interface IOrder
{
    string Id { get; }
    string Comment { get; }
    double TotalQuantity { get; }
    OrderStatus Status { get; }
    void Cancel();
    bool IsReduceOnly {  get; }
}

// 2. Your Wrapper (Simple, no compilation errors, no strictness fighting)
public class OrderWrapper : IOrder
{
    private readonly Order _order;
    public OrderWrapper(Order order) => _order = order;

    public string Id => _order.Id;
    public string Comment
    {
        get => _order.Comment;
    }
    public double TotalQuantity
    {
        get => _order.TotalQuantity;
    }
    public OrderStatus Status => _order.Status;
    public void Cancel() => _order.Cancel();
    public bool IsReduceOnly
    {
        get
        {
            // Safety check: specific adapters might leave this null
            if (_order.AdditionalInfo == null) return false;

            // Scan the collection for the flag we found in the screenshot
            return _order.AdditionalInfo.Any(item =>
                item.Id == "Reduce-Only" &&
                item.Value?.ToString() == "True");
        }
    }
}