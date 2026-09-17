namespace IfItQuacks.Sample.Assignability;

[DuckShape]
public interface IInventory
{
    IEnumerable<string> Items { get; }
    long Count();
    void Add(string item);
}

public class Warehouse
{
    public IList<string> Items { get; } = [];
    public int Count() => Items.Count;
    public bool Add(object item)
    {
        Items.Add(item.ToString()!);
        return true;
    }
}

[DuckShape]
public interface ICoordinate
{
    double X { get; }
    double Y { get; }
}

public class LegacyPoint
{
    public int X;
    public int Y;
}
