using IfItQuacks;
using IfItQuacks.Sample.Conversion;

var customer = new Customer { Name = "Steven", Email = "steven@example.com", InternalNotes = "VIP" };

List<INamed> named =
[
    Duck.As<INamed>(customer),
    Duck.As<INamed>(new Product("Rubber duck", 4.99m)),
    Duck.As<INamed>(new Tag()),
];

foreach (var item in named)
    Console.WriteLine(item.Name);

var view = Duck.As<ICustomerView>(customer);
customer.Email = "quack@example.com";
Console.WriteLine($"{view.Name} <{view.Email}>");
