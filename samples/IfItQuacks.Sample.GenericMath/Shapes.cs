namespace IfItQuacks.Sample.GenericMath;

/// <summary>A generic math style interface: the members are static and refer to the implementing type.</summary>
public interface IAddable<T>
    where T : IAddable<T>
{
    static abstract T Zero { get; }

    static abstract T operator +(T left, T right);

    static abstract T Add(T left, T right);
}

/// <summary>Has the operator and the property, but doesn't implement <see cref="IAddable{T}"/>.</summary>
public readonly record struct Money(decimal Amount)
{
    public static Money Zero => new(0m);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money Add(Money left, Money right) => left + right;

    public override string ToString() => $"{Amount:0.00} EUR";
}

public static partial class Ops
{
    // The constraint is duck-typed: any type with a matching + and Zero can be passed.
    [DuckTyped]
    public static T Sum<T>(T first, T second)
        where T : IAddable<T> => T.Zero + first + second;
}

public static partial class Ops
{
    // System.Numerics.IAdditionOperators is satisfied by the operator alone.
    [DuckTyped]
    public static T SumNumbers<T>(T first, T second)
        where T : System.Numerics.IAdditionOperators<T, T, T> => first + second;
}
