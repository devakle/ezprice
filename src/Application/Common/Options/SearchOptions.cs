using EZPrice.Application.Common.Queues;

namespace EZPrice.Application.Common.Options;

public class SearchOptions
{
    public const string SectionName = "Search";

    public int CacheTtlSeconds { get; set; } = 300;

    public int PageSize { get; set; } = 24;

    public string[] Sources { get; set; } = new[] { SearchSources.MercadoLibre, SearchSources.Amazon };
}
