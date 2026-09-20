namespace IfItQuacks.Sample.MappedShapes;

/// <summary>A domain entity with members no caller outside the domain should see.</summary>
public sealed class Customer
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Steven";
    public string Email { get; set; } = "steven@example.com";
    public string PasswordHash { get; set; } = "secret";
    public string InternalNotes { get; set; } = "pays late";
}

/// <summary>Everything except the two members the outside world has no business seeing - TypeScript's <c>Omit</c>.</summary>
[DuckShape<Customer>(Omit = [nameof(Customer.PasswordHash), nameof(Customer.InternalNotes)], Readonly = true)]
public partial interface ICustomerView;

/// <summary>Only the two members that identify a customer - TypeScript's <c>Pick</c>.</summary>
[DuckShape<Customer>(Pick = [nameof(Customer.Id), nameof(Customer.Name)], Readonly = true)]
public partial interface ICustomerKey;

/// <summary>Every member nullable - TypeScript's <c>Partial</c>, for a PATCH payload.</summary>
[DuckShape<Customer>(Omit = [nameof(Customer.PasswordHash), nameof(Customer.InternalNotes)], Optional = true, Readonly = true)]
public partial interface ICustomerPatch;

public interface IReadable
{
    string Read();
}

public interface IWritable
{
    void Write(string value);
}

/// <summary>Two interfaces intersected into one - TypeScript's <c>A &amp; B</c>.</summary>
[DuckShape<IReadable>(IncludeMethods = true)]
[DuckShape<IWritable>(IncludeMethods = true)]
public partial interface ITextPort;

/// <summary>A DTO that happens to carry the same two members, so it is an <see cref="ICustomerKey"/> too.</summary>
public sealed record CustomerRow(int Id, string Name);

/// <summary>Fits <see cref="ITextPort"/> without implementing either interface.</summary>
public sealed class TextBuffer
{
    private string _value = "";

    public string Read() => _value;

    public void Write(string value) => _value = value;
}

public static partial class Ops
{
    [DuckTyped]
    public static string Render(ICustomerView view) => $"{view.Id} {view.Name} <{view.Email}>";

    [DuckTyped]
    public static string Key(ICustomerKey key) => $"{key.Id}:{key.Name}";

    [DuckTyped]
    public static string Describe(ICustomerPatch patch) =>
        $"{patch.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} {patch.Name ?? "-"} {patch.Email ?? "-"}";

    [DuckTyped]
    public static string RoundTrip(ITextPort stream)
    {
        stream.Write("quack");
        return stream.Read();
    }
}
