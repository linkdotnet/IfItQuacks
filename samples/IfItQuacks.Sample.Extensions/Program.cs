using IfItQuacks.Sample.Extensions;

// Neither Person nor Mallard implements INamed - the call is still checked at compile time.
Console.WriteLine(new Person().Greet());
Console.WriteLine(new Mallard().Greet("Quack"));

// Anonymous types are ducks too, which makes them usable as a receiver.
Console.WriteLine(new { Name = "Anonymous" }.Greet());

// The receiver and a second interface parameter are duck-typed independently.
var product = new Product("Rubber duck", 4.99m);
Console.WriteLine(product.Label(product));

// Calling it as a plain static method works as well.
Console.WriteLine(Ops.Greet(new Person(), "Hi"));

// A conditional access is intercepted like any other call.
Console.WriteLine(Find("Steven")?.Greet() ?? "<nobody>");
Console.WriteLine(Find("nobody")?.Greet() ?? "<nobody>");

static Person? Find(string name) => name == "Steven" ? new Person() : null;
