using IfItQuacks;
using IfItQuacks.Sample.Members;

var row = new Row();
var grid = Duck.As<IGrid>(row);

grid.Changed += (_, e) => Console.WriteLine($"changed to {e.Value}");

// The indexer is forwarded in both directions.
grid[1] = 20;
Console.WriteLine($"grid[1] = {grid[1]}");

// A ref return aliases the original array, so writing through the view changes the row.
grid.First() = 10;
Console.WriteLine($"row.First() = {row.First()}");

// The default interface member runs because Row has no Describe.
Console.WriteLine(grid.Describe());

// LoudRow has one, so its own implementation is forwarded instead.
Console.WriteLine(Duck.As<IGrid>(new LoudRow()).Describe());
