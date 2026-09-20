namespace IfItQuacks.Benchmarks;

public static partial class Ops
{
    [DuckTyped]
    public static int Describe(IPerson person) => person.Name.Length + person.Age;

    [DuckTyped]
    public static int DescribeProduct(IProduct product) => product.Name.Length + (int)product.Price;

    public static int DescribeConcrete(Person person) => person.Name.Length + person.Age;

    public static int DescribeInterface(IPerson person) => person.Name.Length + person.Age;

    /// <summary>The same body, but the duck type is a constrained type parameter: the adapter is passed
    /// as a type argument instead of an interface, so nothing is boxed and the JIT specializes per shape.</summary>
    [DuckTyped]
    public static int DescribeConstrained<T>(T person) where T : IPerson => person.Name.Length + person.Age;

    [DuckTyped]
    public static int DescribeProductConstrained<T>(T product) where T : IProduct => product.Name.Length + (int)product.Price;
}
