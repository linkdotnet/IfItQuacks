namespace IfItQuacks.Benchmarks;

public static partial class Ops
{
    [DuckTyped]
    public static int Describe(IPerson person) => person.Name.Length + person.Age;

    [DuckTyped]
    public static int DescribeProduct(IProduct product) => product.Name.Length + (int)product.Price;

    public static int DescribeConcrete(Person person) => person.Name.Length + person.Age;

    public static int DescribeInterface(IPerson person) => person.Name.Length + person.Age;
}
