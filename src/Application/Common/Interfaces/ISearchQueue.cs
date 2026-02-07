using EZPrice.Application.Search.Models;

namespace EZPrice.Application.Common.Interfaces;

public interface ISearchQueue
{
    Task EnqueueAsync(SearchJob job, CancellationToken cancellationToken);
}
