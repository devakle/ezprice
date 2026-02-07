using EZPrice.Application.Search.Models;

namespace EZPrice.Application.Common.Interfaces;

public interface ISourceScraper
{
    string Source { get; }

    Task<IReadOnlyList<SearchResultItem>> ScrapeAsync(SearchJob job, CancellationToken cancellationToken);
}
