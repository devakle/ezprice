namespace EZPrice.Application.Search.Models;

public class SearchResultsVm
{
    public string Query { get; set; } = string.Empty;

    public int Page { get; set; }

    public List<SearchResultItem> Items { get; set; } = new();

    public List<SearchSourceStatus> Sources { get; set; } = new();

    public string RequestId { get; set; } = string.Empty;
}
