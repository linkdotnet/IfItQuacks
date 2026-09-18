using IfItQuacks;
using IfItQuacks.Sample.Testing;

// A member holding a delegate satisfies an interface method, so a stub is an object literal.
var clock = Duck.As<IClock>(new { UtcNow = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc) });
Console.WriteLine($"clock: {clock.UtcNow:yyyy-MM-dd}");

// Duck.Stub fills in what is missing: those members throw when they are used.
var repository = Duck.Stub<IOrderRepository>(new
{
    Find = (Func<int, Order?>)(id => new Order(id, "Stubbed")),
});
Console.WriteLine($"stub: {repository.Find(7)}");

try
{
    repository.Save(new Order(2, "Never saved"));
}
catch (DuckStubException e)
{
    Console.WriteLine($"stub: {e.Message}");
}

// Duck.Merge replaces a single member of a real object - a spy, without a mocking library.
Order? saved = null;
var real = new InMemoryOrders();
var spy = Duck.Merge<IOrderRepository>(new { Save = (Action<Order>)(order => saved = order) }, real);

spy.Save(new Order(2, "Intercepted"));
Console.WriteLine($"spy: saw {saved}, the real repository still holds {real.Count} order(s)");
Console.WriteLine($"spy: reads still go to the real one: {spy.Find(1)}");
