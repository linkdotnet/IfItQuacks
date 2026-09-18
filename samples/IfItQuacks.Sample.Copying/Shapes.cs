namespace IfItQuacks.Sample.Copying;

// The entity a repository would hand you, with more on it than you want to expose.
public class Customer
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public string InternalNotes { get; set; } = "";
}

// A record fills its primary constructor from the members of the same name.
public record CustomerDto(string Name, string Email);

// A class with settable properties is filled through an object initializer.
public class CustomerRow
{
    public string Name { get; set; } = "";
    public long Age { get; set; }
}
