using IfItQuacks;
using IfItQuacks.Sample.Delegates;

// A delegate variable.
Func<int, string> hex = value => $"0x{value:X}";
Console.WriteLine(Ops.Render(hex, 255));

// A lambda, as long as its parameter types are explicit.
Console.WriteLine(Ops.Render((int value) => $"{value} EUR", 42));

// A method group.
Console.WriteLine(Ops.Render(Spell, 3));

// Duck.As turns a delegate into the interface, e.g. to store it.
IFormatter formatter = Duck.As<IFormatter>(hex);
Console.WriteLine(formatter.Format(4095));

// A [DuckTyped] method itself converts to a delegate over the duck type.
Func<Person, string> describe = Ops.Describe;
Console.WriteLine(describe(new Person()));
Console.WriteLine(string.Join(", ", new[] { new Person() }.Select(Ops.Describe)));

static string Spell(int value) => value switch { 1 => "one", 2 => "two", 3 => "three", _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
