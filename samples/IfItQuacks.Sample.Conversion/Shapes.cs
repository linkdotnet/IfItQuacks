namespace IfItQuacks.Sample.Conversion;

public interface INamed
{
    string Name { get; }
}

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

    public override string ToString() => Name;
}

public readonly record struct Product(string Name, decimal Price);

public class Tag : INamed
{
    public string Name => "duck";
}
