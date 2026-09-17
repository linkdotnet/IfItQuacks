using IfItQuacks;
using IfItQuacks.Sample.FrameworkInterfaces;

Report.Print(new Countdown(3));
Report.Print([1, 2, 3]);

using (Duck.As<IDisposable>(new TemporaryFile("quack.tmp")))
{
    Console.WriteLine("Working with the file");
}
