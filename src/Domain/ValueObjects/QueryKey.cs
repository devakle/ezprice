using EZPrice.Domain.Common;

namespace EZPrice.Domain.ValueObjects;

public sealed class QueryKey : ValueObject
{
    private QueryKey(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static QueryKey From(string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            throw new ArgumentException("Query cannot be empty.", nameof(normalizedQuery));
        }

        return new QueryKey(normalizedQuery);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
