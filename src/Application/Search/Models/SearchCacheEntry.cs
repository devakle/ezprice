namespace EZPrice.Application.Search.Models;

public class SearchCacheEntry
{
    public DateTimeOffset CachedAt { get; set; }

    public int TtlSeconds { get; set; }

    public List<SearchResultItem> Items { get; set; } = new();

    public List<SearchSourceStatus> Sources { get; set; } = new();

    public bool IsStale(DateTimeOffset now) => now - CachedAt > TimeSpan.FromSeconds(TtlSeconds);
}
