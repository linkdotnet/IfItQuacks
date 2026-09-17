using IfItQuacks.Sample.GenericMath;

// Money satisfies IAddable<Money> structurally, so the constrained method accepts it.
Console.WriteLine(Ops.Sum(new Money(19.99m), new Money(5.01m)));

// The same works for BCL generic math interfaces.
Console.WriteLine(Ops.SumNumbers(new Money(1m), new Money(2m)));
