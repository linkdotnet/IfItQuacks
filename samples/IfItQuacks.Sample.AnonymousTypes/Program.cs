using IfItQuacks;
using IfItQuacks.Sample.AnonymousTypes;

Printer.Print(new { Name = "Steven", Age = 42 });

IPerson stub = Duck.As<IPerson>(new { Name = "Donald", Age = 90, Hometown = "Duckburg" });
Printer.Print(stub);
