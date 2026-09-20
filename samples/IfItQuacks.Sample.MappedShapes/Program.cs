using IfItQuacks;
using IfItQuacks.Sample.MappedShapes;

var customer = new Customer();

// ICustomerView is generated from Customer minus two members. Customer satisfies it without knowing it exists.
Console.WriteLine(Ops.Render(customer));

// The same derived interface is satisfied by a DTO and by an object literal - that is what makes
// Pick/Omit useful in C# at all: a derived interface nothing implements would be dead weight.
Console.WriteLine(Ops.Key(customer));
Console.WriteLine(Ops.Key(new CustomerRow(2, "Donald")));
Console.WriteLine(Ops.Key(new { Id = 3, Name = "Daisy" }));

// Optional makes every member nullable, so a partial payload fits the same shape as the entity.
Console.WriteLine(Ops.Describe(customer));
Console.WriteLine(Ops.Describe(new { Id = (int?)null, Name = "Donald", Email = (string?)null }));

// Two interfaces intersected into one.
Console.WriteLine(Ops.RoundTrip(new TextBuffer()));

// A derived shape works everywhere an interface does.
ICustomerView view = Duck.As<ICustomerView>(customer);
customer.Name = "Steven G.";
Console.WriteLine($"the view forwards: {view.Name}");
