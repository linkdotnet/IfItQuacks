using IfItQuacks.Sample.Constraints;

// Person does not implement INamed. The call binds to a generated overload that passes the adapter
// struct as the type argument - see Generated/.../IfItQuacks.Overloads.Ops.Greet.g.cs.
Console.WriteLine(Ops.Greet(new Person()));

// A readonly struct works the same way, and is not boxed either.
Console.WriteLine(Ops.Greet(new Product("Rubber duck", 4.99m)));

// Two constrained parameters, two different shapes.
Console.WriteLine(Ops.Introduce(new Person(), new Mallard()));

// A constrained parameter next to an ordinary interface parameter: Recorder fits ILog structurally.
var recorder = new Recorder();
Console.WriteLine(Ops.Label(new Product("Rubber duck", 4.99m), recorder));
Console.WriteLine($"recorded: {string.Join(", ", recorder.Lines)}");

// Extension form.
Console.WriteLine(new Mallard().Shout());
