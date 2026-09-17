namespace IfItQuacks.Sample.Conversion;

[DuckShape]
public interface INamed
{
    string Name { get; }
}

[DuckShape]
public interface ICustomerView
{
    string Name { get; }
    string Email { get; }
}

public class Customer
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string InternalNotes { get; set; } = "";
}

public readonly record struct Product(string Name, decimal Price);

public class Tag : INamed
{
    public string Name => "duck";
}
