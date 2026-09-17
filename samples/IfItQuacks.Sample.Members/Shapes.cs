namespace IfItQuacks.Sample.Members;

public sealed class CellChangedEventArgs : EventArgs
{
    public int Value { get; init; }
}

public interface IGrid
{
    // An event: the delegate type has to match exactly.
    event EventHandler<CellChangedEventArgs>? Changed;

    // An indexer, here read-write.
    int this[int index] { get; set; }

    // A by-ref member aliases the original storage instead of copying it.
    ref int First();

    // A default interface member is optional: it is forwarded when the type has a match.
    string Describe() => $"grid with first={First()}";
}

/// <summary>A type that fits <see cref="IGrid"/> without knowing about it.</summary>
public class Row
{
    private readonly int[] _cells = [1, 2, 3];

    public event EventHandler<CellChangedEventArgs>? Changed;

    public int this[int index]
    {
        get => _cells[index];
        set
        {
            _cells[index] = value;
            Changed?.Invoke(this, new CellChangedEventArgs { Value = value });
        }
    }

    public ref int First() => ref _cells[0];
}

/// <summary>The same shape, but with its own <c>Describe</c>, which wins over the default implementation.</summary>
public class LoudRow
{
    private readonly int[] _cells = [7, 8, 9];

    public event EventHandler<CellChangedEventArgs>? Changed;

    public int this[int index]
    {
        get => _cells[index];
        set
        {
            _cells[index] = value;
            Changed?.Invoke(this, new CellChangedEventArgs { Value = value });
        }
    }

    public ref int First() => ref _cells[0];

    public string Describe() => $"LOUD GRID ({_cells[0]})";
}
