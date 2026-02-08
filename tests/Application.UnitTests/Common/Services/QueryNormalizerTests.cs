using EZPrice.Application.Common.Services;
using NUnit.Framework;
using Shouldly;

namespace EZPrice.Application.UnitTests.Common.Services;

public class QueryNormalizerTests
{
    [Test]
    public void Normalize_ReturnsEmpty_WhenQueryIsNullOrWhitespace()
    {
        var normalizer = new QueryNormalizer();

        normalizer.Normalize(null!).ShouldBe(string.Empty);
        normalizer.Normalize("").ShouldBe(string.Empty);
        normalizer.Normalize("   ").ShouldBe(string.Empty);
    }

    [Test]
    public void Normalize_Trims_Lowercases_And_RemovesDiacritics()
    {
        var normalizer = new QueryNormalizer();

        var result = normalizer.Normalize("  Café ÁRBOLES  ");

        result.ShouldBe("cafe arboles");
    }

    [Test]
    public void Normalize_PreservesNonDiacriticCharacters()
    {
        var normalizer = new QueryNormalizer();

        var result = normalizer.Normalize("Price 123-XYZ");

        result.ShouldBe("price 123-xyz");
    }
}
