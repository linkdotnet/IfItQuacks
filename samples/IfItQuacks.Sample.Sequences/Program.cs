using IfItQuacks;
using IfItQuacks.Sample.Sequences;

// A List<Person> is not an IEnumerable<INamed>, but every element fits - so the sequence is adapted element by element.
List<Person> people = [new("Steven"), new("Donald")];
Console.WriteLine(Ops.Join(people));

// Arrays work the same way.
Console.WriteLine(Ops.Join(new[] { new Mallard() }));

// IReadOnlyList<T> keeps Count and the indexer, both forwarded to the source.
Console.WriteLine(Ops.Second(people));

// Duck.As returns a view: adding to the list is visible through it.
var view = Duck.As<IReadOnlyList<INamed>>(people);
people.Add(new Person("Daisy"));
Console.WriteLine($"{view.Count}: {string.Join(", ", view.Select(p => p.Name))}");
