namespace IfItQuacks.Sample.Extensions;

// A [DuckTyped] extension method reads like any other one, but its receiver only has to fit the interface.
public static partial class Ops
{
    [DuckTyped]
    public static string Greet(this INamed named, string greeting = "Hello") => $"{greeting}, {named.Name}!";

    [DuckTyped]
    public static string Label(this INamed named, IPriced priced) => $"{named.Name}: {priced.Price:0.00}";
}
