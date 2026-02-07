namespace EZPrice.Application.Search.Models;

public class SearchIndexResult
{
    public List<SearchResultItem> Items { get; set; } = new();

    public int Total { get; set; }
}
