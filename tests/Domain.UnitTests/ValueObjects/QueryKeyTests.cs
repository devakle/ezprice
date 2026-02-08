using EZPrice.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace EZPrice.Domain.UnitTests.ValueObjects;

public class QueryKeyTests
{
    [Test]
    public void From_ReturnsValueObject_WhenQueryIsValid()
    {
        var key = QueryKey.From("normalized-query");

        key.Value.ShouldBe("normalized-query");
        key.ToString().ShouldBe("normalized-query");
    }

    [Test]
    public void From_Throws_WhenQueryIsNullOrWhitespace()
    {
        Should.Throw<ArgumentException>(() => QueryKey.From(null!));
        Should.Throw<ArgumentException>(() => QueryKey.From(""));
        Should.Throw<ArgumentException>(() => QueryKey.From("   "));
    }
}
