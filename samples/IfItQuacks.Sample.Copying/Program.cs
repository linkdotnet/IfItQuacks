using IfItQuacks;
using IfItQuacks.Sample.Copying;

var customer = new Customer { Name = "Steven", Email = "steven@example.com", Age = 40, InternalNotes = "VIP" };

// Duck.As gives a view that forwards; Duck.To copies once and never looks back.
var dto = Duck.To<CustomerDto>(customer);
var row = Duck.To<CustomerRow>(customer);   // int Age widens to long

customer.Name = "changed";
Console.WriteLine($"{dto} - the copy kept '{dto.Name}'");
Console.WriteLine($"{row.Name} is {row.Age}");

// Anonymous types are a source like any other, which makes fixtures short.
Console.WriteLine(Duck.To<CustomerDto>(new { Name = "Donald", Email = "donald@example.com" }));
