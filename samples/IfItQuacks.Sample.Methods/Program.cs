using IfItQuacks.Sample.Methods;

Ops.Foo(new A());
Ops.Foo(new B(), new ConsoleLog());
Ops.Foo(new B(), new PrefixLog());

var greeter = new Greeter();
Console.WriteLine(greeter.Greet(new Person(), new Mallard()));
Console.WriteLine(greeter.Greet(new Mallard(), new Person(), greeting: "Quack"));
