using EZPrice.Domain.ValueObjects;
using EZPrice.Application.Search.Models;

namespace EZPrice.Application.Common.Interfaces;

public interface ISearchCache
{
    Task<SearchCacheEntry?> GetAsync(QueryKey queryKey, int page, CancellationToken cancellationToken);

    Task SetAsync(QueryKey queryKey, int page, SearchCacheEntry entry, TimeSpan ttl, CancellationToken cancellationToken);
}
