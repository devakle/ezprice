using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Common.Options;
using EZPrice.Application.Search.Models;
using EZPrice.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using System.Linq;

namespace EZPrice.Application.Search.Queries;

public record GetSearchResultsQuery(string Query, int Page = 1, SearchSortOrder Sort = SearchSortOrder.None)
    : IRequest<SearchResultsVm>;

public class GetSearchResultsQueryHandler : IRequestHandler<GetSearchResultsQuery, SearchResultsVm>
{
    private readonly ISearchCache _cache;
    private readonly ISearchIndex _index;
    private readonly ISearchQueue _queue;
    private readonly ISearchJobDeduper _deduper;
    private readonly IQueryNormalizer _normalizer;
    private readonly SearchOptions _options;
    private readonly TimeProvider _timeProvider;

    public GetSearchResultsQueryHandler(
        ISearchCache cache,
        ISearchIndex index,
        ISearchQueue queue,
        ISearchJobDeduper deduper,
        IQueryNormalizer normalizer,
        IOptions<SearchOptions> options,
        TimeProvider timeProvider)
    {
        _cache = cache;
        _index = index;
        _queue = queue;
        _deduper = deduper;
        _normalizer = normalizer;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<SearchResultsVm> Handle(GetSearchResultsQuery request, CancellationToken cancellationToken)
    {
        var normalized = _normalizer.Normalize(request.Query);
        var queryKey = QueryKey.From(normalized);
        var now = _timeProvider.GetUtcNow();
        var requestId = Guid.NewGuid().ToString("N");

        var cached = await _cache.GetAsync(queryKey, request.Page, cancellationToken);
        if (cached is not null)
        {
            if (cached.IsStale(now))
            {
                await EnqueueRefreshAsync(request.Query, queryKey.Value, request.Page, now, cancellationToken);
                UpdateStatuses(cached.Sources, SearchSourceStates.Refreshing, now - cached.CachedAt);
            }
            else
            {
                UpdateStatuses(cached.Sources, SearchSourceStates.Ok, now - cached.CachedAt);
            }

            var items = await ResolveItemsAsync(
                request,
                queryKey.Value,
                cached.Items,
                cancellationToken);

            return new SearchResultsVm
            {
                Query = request.Query,
                Page = request.Page,
                Items = items,
                Sources = cached.Sources,
                RequestId = requestId
            };
        }

        var indexResults = await _index.SearchAsync(
            queryKey.Value,
            request.Query,
            request.Page,
            _options.PageSize,
            request.Sort,
            cancellationToken);
        var sources = BuildSources(SearchSourceStates.Pending, TimeSpan.Zero, request.Page);

        await EnqueueRefreshAsync(request.Query, queryKey.Value, request.Page, now, cancellationToken);

        var sortedItems = ApplySort(indexResults.Items, request.Sort);

        var entry = new SearchCacheEntry
        {
            CachedAt = now,
            TtlSeconds = _options.CacheTtlSeconds,
            Items = sortedItems,
            Sources = sources
        };

        await _cache.SetAsync(queryKey, request.Page, entry, TimeSpan.FromSeconds(_options.CacheTtlSeconds), cancellationToken);

        return new SearchResultsVm
        {
            Query = request.Query,
            Page = request.Page,
            Items = sortedItems,
            Sources = sources,
            RequestId = requestId
        };
    }

    private async Task<List<SearchResultItem>> ResolveItemsAsync(
        GetSearchResultsQuery request,
        string queryKey,
        List<SearchResultItem> cachedItems,
        CancellationToken cancellationToken)
    {
        if (!IsGlobalSort(request.Sort))
        {
            return ApplySort(cachedItems, request.Sort);
        }

        var indexResults = await _index.SearchAsync(
            queryKey,
            request.Query,
            request.Page,
            _options.PageSize,
            request.Sort,
            cancellationToken);

        if (indexResults.Items.Count > 0)
        {
            return indexResults.Items;
        }

        return ApplySort(cachedItems, request.Sort);
    }

    private static bool IsGlobalSort(SearchSortOrder sort)
    {
        return sort is SearchSortOrder.PriceAsc or SearchSortOrder.PriceDesc;
    }

    private async Task EnqueueRefreshAsync(string query, string queryKey, int page, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var source in _options.Sources)
        {
            var job = new SearchJob(source, query, queryKey, page, now);
            if (await _deduper.TryAcquireAsync(job, TimeSpan.FromMinutes(2), cancellationToken))
            {
                await _queue.EnqueueAsync(job, cancellationToken);
            }
        }
    }

    private List<SearchSourceStatus> BuildSources(string status, TimeSpan freshness, int page)
    {
        return _options.Sources
            .Select(source => new SearchSourceStatus(
                source,
                status,
                (int)freshness.TotalSeconds,
                true,
                $"{source}:{page + 1}"))
            .ToList();
    }

    private static void UpdateStatuses(List<SearchSourceStatus> sources, string status, TimeSpan freshness)
    {
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            sources[i] = source with { Status = status, FreshnessSeconds = (int)freshness.TotalSeconds };
        }
    }

    private static List<SearchResultItem> ApplySort(IEnumerable<SearchResultItem> items, SearchSortOrder sort)
    {
        return sort switch
        {
            SearchSortOrder.PriceAsc => items
                .OrderBy(item => item.PriceAmount)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SearchSortOrder.PriceDesc => items
                .OrderByDescending(item => item.PriceAmount)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => items.ToList()
        };
    }
}
