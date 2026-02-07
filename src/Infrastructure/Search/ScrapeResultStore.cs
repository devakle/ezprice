using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Common.Options;
using EZPrice.Application.Search.Models;
using EZPrice.Domain.Entities;
using EZPrice.Domain.ValueObjects;
using EZPrice.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Linq;

namespace EZPrice.Infrastructure.Search;

public class ScrapeResultStore : IScrapeResultStore
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISearchCache _cache;
    private readonly ISearchIndex _index;
    private readonly SearchOptions _options;
    private readonly TimeProvider _timeProvider;

    public ScrapeResultStore(
        ApplicationDbContext dbContext,
        ISearchCache cache,
        ISearchIndex index,
        IOptions<SearchOptions> options,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _cache = cache;
        _index = index;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task PersistAsync(SearchJob job, IReadOnlyList<SearchResultItem> items, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var item in items)
        {
            _dbContext.Offers.Add(new Offer
            {
                Query = job.Query,
                QueryKey = job.QueryKey,
                Source = item.Source,
                Title = item.Title,
                PriceAmount = item.PriceAmount,
                Currency = item.Currency,
                Url = item.Url,
                ImageUrl = item.ImageUrl,
                Page = job.Page,
                ScrapedAt = item.ScrapedAt
            });
        }

        await UpsertSearchQueryAsync(job, now, cancellationToken);
        await SaveChangesWithQueryKeyRetryAsync(job, now, cancellationToken);

        await _index.UpsertAsync(job, items, cancellationToken);
        await UpdateCacheAsync(job, items, now, cancellationToken);
    }

    private async Task UpdateCacheAsync(SearchJob job, IReadOnlyList<SearchResultItem> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var queryKey = QueryKey.From(job.QueryKey);
        var entry = await _cache.GetAsync(queryKey, job.Page, cancellationToken)
            ?? new SearchCacheEntry
            {
                CachedAt = now,
                TtlSeconds = _options.CacheTtlSeconds,
                Items = new List<SearchResultItem>(),
                Sources = _options.Sources.Select(source => new SearchSourceStatus(
                    source,
                    SearchSourceStates.Pending,
                    0,
                    true,
                    $"{source}:{job.Page + 1}")).ToList()
            };

        entry.CachedAt = now;
        entry.TtlSeconds = _options.CacheTtlSeconds;
        entry.Items.RemoveAll(item => item.Source == job.Source);
        entry.Items.AddRange(items);

        for (var i = 0; i < entry.Sources.Count; i++)
        {
            var source = entry.Sources[i];
            if (string.Equals(source.Name, job.Source, StringComparison.OrdinalIgnoreCase))
            {
                entry.Sources[i] = source with
                {
                    Status = SearchSourceStates.Ok,
                    FreshnessSeconds = 0,
                    NextPageToken = $"{job.Source}:{job.Page + 1}",
                    HasMore = true
                };
            }
        }

        await _cache.SetAsync(queryKey, job.Page, entry, TimeSpan.FromSeconds(_options.CacheTtlSeconds), cancellationToken);
    }

    private async Task UpsertSearchQueryAsync(SearchJob job, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SearchQueries
            .SingleOrDefaultAsync(q => q.QueryKey == job.QueryKey, cancellationToken);

        if (existing is null)
        {
            _dbContext.SearchQueries.Add(new SearchQuery
            {
                Query = job.Query,
                QueryKey = job.QueryKey,
                LastRequestedAt = job.RequestedAt,
                LastRefreshedAt = now
            });
        }
        else
        {
            existing.Query = job.Query;
            existing.LastRequestedAt = job.RequestedAt;
            existing.LastRefreshedAt = now;
        }
    }

    private async Task SaveChangesWithQueryKeyRetryAsync(SearchJob job, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSearchQueryUniqueViolation(ex))
        {
            var addedQuery = _dbContext.ChangeTracker.Entries<SearchQuery>()
                .FirstOrDefault(e => e.State == EntityState.Added && e.Entity.QueryKey == job.QueryKey);
            if (addedQuery is not null)
            {
                addedQuery.State = EntityState.Detached;
            }

            var existing = await _dbContext.SearchQueries
                .SingleOrDefaultAsync(q => q.QueryKey == job.QueryKey, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            existing.Query = job.Query;
            existing.LastRequestedAt = job.RequestedAt;
            existing.LastRefreshedAt = now;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsSearchQueryUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is not SqliteException sqliteEx)
        {
            return false;
        }

        return sqliteEx.SqliteErrorCode == 19
            && sqliteEx.Message.Contains("UNIQUE constraint failed: SearchQueries.QueryKey", StringComparison.OrdinalIgnoreCase);
    }
}
