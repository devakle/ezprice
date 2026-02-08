using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.QueryDsl;
using EZPrice.Application.Common.Interfaces;
using EZPrice.Application.Common.Options;
using EZPrice.Application.Search.Models;
using Microsoft.Extensions.Options;

namespace EZPrice.Infrastructure.Search;

public class ElasticsearchIndex : ISearchIndex
{
    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchOptions _options;

    public ElasticsearchIndex(ElasticsearchClient client, IOptions<ElasticsearchOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<SearchIndexResult> SearchAsync(
        string queryKey,
        string query,
        int page,
        int pageSize,
        SearchSortOrder sort,
        CancellationToken cancellationToken)
    {
        await EnsureIndexAsync(cancellationToken);

        var from = Math.Max(0, (page - 1) * pageSize);

        var request = new SearchRequestDescriptor<OfferDocument>()
            .Index(_options.IndexName)
            .From(from)
            .Size(pageSize)
            .Query(q => q
                .Bool(b => b
                    .Filter(f => f.Term(t => t.Field(doc => doc.QueryKey).Value(queryKey)))
                    .Must(m => m.Match(mt => mt.Field(doc => doc.Title).Query(query)))
                ));

        request = sort switch
        {
            SearchSortOrder.PriceAsc => request.Sort(so => so.Field(f => f.PriceAmount, fs => fs.Order(SortOrder.Asc))),
            SearchSortOrder.PriceDesc => request.Sort(so => so.Field(f => f.PriceAmount, fs => fs.Order(SortOrder.Desc))),
            _ => request
        };

        var response = await _client.SearchAsync(request, cancellationToken);

        if (!response.IsValidResponse)
        {
            return new SearchIndexResult();
        }

        var items = response.Documents.Select(doc => new SearchResultItem(
            doc.Title,
            doc.PriceAmount,
            doc.Currency,
            doc.Url,
            doc.Source,
            doc.ScrapedAt,
            doc.ImageUrl)).ToList();

        var total = response.Total;
        if (total == 0 && items.Count > 0)
        {
            total = items.Count;
        }

        return new SearchIndexResult
        {
            Items = items,
            Total = (int)Math.Min(total, int.MaxValue)
        };
    }

    public async Task UpsertAsync(SearchJob job, IEnumerable<SearchResultItem> items, CancellationToken cancellationToken)
    {
        await EnsureIndexAsync(cancellationToken);

        var documents = items.Select(item => new OfferDocument
        {
            Id = BuildDocumentId(job.QueryKey, item.Source, item.Url),
            QueryKey = job.QueryKey,
            Query = job.Query,
            Source = item.Source,
            Title = item.Title,
            PriceAmount = item.PriceAmount,
            Currency = item.Currency,
            Url = item.Url,
            ImageUrl = item.ImageUrl,
            ScrapedAt = item.ScrapedAt
        }).ToList();

        if (documents.Count == 0)
        {
            return;
        }

        await _client.BulkAsync(b => b
            .Index(_options.IndexName)
            .IndexMany(documents, (descriptor, doc) => descriptor.Id(doc.Id)),
            cancellationToken);
    }

    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        var exists = await _client.Indices.ExistsAsync(_options.IndexName, cancellationToken);
        if (exists.Exists)
        {
            return;
        }

        var createRequest = new CreateIndexRequest(_options.IndexName);

        await _client.Indices.CreateAsync(createRequest, cancellationToken);
    }

    private static string BuildDocumentId(string queryKey, string source, string url)
    {
        var input = $"{queryKey}|{source}|{url}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
