using IfItQuacks;
using IfItQuacks.Sample.Assignability;

var inventory = Duck.As<IInventory>(new Warehouse());
inventory.Add("Rubber duck");
Console.WriteLine($"{inventory.Count()}: {string.Join(", ", inventory.Items)}");

var point = Duck.As<ICoordinate>(new LegacyPoint { X = 3, Y = 4 });
Console.WriteLine(Math.Sqrt((point.X * point.X) + (point.Y * point.Y)));
