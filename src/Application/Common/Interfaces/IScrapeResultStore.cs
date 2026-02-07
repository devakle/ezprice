using EZPrice.Application.Search.Models;

namespace EZPrice.Application.Common.Interfaces;

public interface IScrapeResultStore
{
    Task PersistAsync(SearchJob job, IReadOnlyList<SearchResultItem> items, CancellationToken cancellationToken);
}
