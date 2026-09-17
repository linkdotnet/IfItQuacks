using IfItQuacks.Sample.Generics;

int number = Ops.Unwrap(new IntBox(42));
string text = Ops.Unwrap(new Box<string>("quack"));
Console.WriteLine($"{number} {text}");
