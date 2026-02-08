using EZPrice.Application.Search.Models;

namespace EZPrice.Application.Common.Interfaces;

public interface ISearchIndex
{
    Task<SearchIndexResult> SearchAsync(
        string queryKey,
        string query,
        int page,
        int pageSize,
        SearchSortOrder sort,
        CancellationToken cancellationToken);

    Task UpsertAsync(SearchJob job, IEnumerable<SearchResultItem> items, CancellationToken cancellationToken);
}
