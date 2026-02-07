using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Search.Models;
using StackExchange.Redis;

namespace EZPrice.Infrastructure.Cache;

public class RedisSearchJobDeduper : ISearchJobDeduper
{
    private readonly IDatabase _database;

    public RedisSearchJobDeduper(IConnectionMultiplexer connection)
    {
        _database = connection.GetDatabase();
    }

    public async Task<bool> TryAcquireAsync(SearchJob job, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = $"search:lock:{job.Source}:{job.QueryKey}:page:{job.Page}";
        return await _database.StringSetAsync(key, "1", ttl, When.NotExists);
    }
}
