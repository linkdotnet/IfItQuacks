namespace IfItQuacks.Sample.Testing;

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface IOrderRepository
{
    Order? Find(int id);
    void Save(Order order);
    int Count { get; }
}

public record Order(int Id, string Product);

// The real implementation the tests replace parts of.
public class InMemoryOrders : IOrderRepository
{
    private readonly Dictionary<int, Order> _orders = new() { [1] = new Order(1, "Rubber duck") };

    public Order? Find(int id) => _orders.GetValueOrDefault(id);

    public void Save(Order order) => _orders[order.Id] = order;

    public int Count => _orders.Count;
}
