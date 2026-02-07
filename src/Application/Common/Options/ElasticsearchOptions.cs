namespace EZPrice.Application.Common.Options;

public class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    public string Uri { get; set; } = "http://localhost:9200";

    public string IndexName { get; set; } = "ezprice-offers";
}
