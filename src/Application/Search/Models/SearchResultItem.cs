namespace EZPrice.Application.Search.Models;

public record SearchResultItem(
    string Title,
    decimal PriceAmount,
    string Currency,
    string Url,
    string Source,
    DateTimeOffset ScrapedAt,
    string? ImageUrl);
