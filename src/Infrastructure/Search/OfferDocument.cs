namespace EZPrice.Infrastructure.Search;

public class OfferDocument
{
    public string Id { get; set; } = string.Empty;
    public string QueryKey { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Url { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTimeOffset ScrapedAt { get; set; }
}
