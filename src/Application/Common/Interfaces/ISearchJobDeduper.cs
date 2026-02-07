using EZPrice.Application.Search.Models;

namespace EZPrice.Application.Common.Interfaces;

public interface ISearchJobDeduper
{
    Task<bool> TryAcquireAsync(SearchJob job, TimeSpan ttl, CancellationToken cancellationToken);
}
