using IfItQuacks;
using IfItQuacks.Sample.Signatures;

var calls = 0;
Ops.TryDescribe(new Person(), ref calls, out var first, "!", "?");
Ops.TryDescribe(new Mallard(), ref calls, out var second);
Console.WriteLine($"{first} / {second} ({calls} calls)");

Console.WriteLine(Ops.ShoutAll(new Person(), new Mallard()));

var greeter = new Greeter();
Console.WriteLine(greeter.Greet(new Person()));
Console.WriteLine(greeter.GreetMallard());

// The struct receiver is passed by reference, so Count keeps counting.
var counter = new Counter();
Console.WriteLine(counter.Add(new Person()));
Console.WriteLine(counter.Add(new Mallard()));
Console.WriteLine($"counter.Count = {counter.Count}");

// Inside a generic method the argument type is unknown, so the call goes to the
// generated fallback: it works for values implementing the interface at runtime.
Console.WriteLine(Ops.DescribeAtRuntime(Duck.As<INamed>(new Person())));

try
{
    Ops.DescribeAtRuntime(new Rock());
}
catch (DuckTypeMismatchException e)
{
    Console.WriteLine(e.Message);
}
