using System.Text.Json;
using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Search.Models;
using EZPrice.Domain.ValueObjects;
using StackExchange.Redis;

namespace EZPrice.Infrastructure.Cache;

public class RedisSearchCache : ISearchCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database;

    public RedisSearchCache(IConnectionMultiplexer connection)
    {
        _database = connection.GetDatabase();
    }

    public async Task<SearchCacheEntry?> GetAsync(QueryKey queryKey, int page, CancellationToken cancellationToken)
    {
        var key = CacheKey(queryKey, page);
        var value = await _database.StringGetAsync(key);
        if (!value.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<SearchCacheEntry>(value.ToString(), SerializerOptions);
    }

    public async Task SetAsync(QueryKey queryKey, int page, SearchCacheEntry entry, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = CacheKey(queryKey, page);
        var payload = JsonSerializer.Serialize(entry, SerializerOptions);
        await _database.StringSetAsync(key, payload, ttl);
    }

    private static string CacheKey(QueryKey queryKey, int page) => $"search:{queryKey.Value}:page:{page}";
}
