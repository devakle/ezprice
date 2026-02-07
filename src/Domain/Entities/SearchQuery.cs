using EZPrice.Domain.Common;

namespace EZPrice.Domain.Entities;

public class SearchQuery : BaseAuditableEntity
{
    public string Query { get; set; } = string.Empty;

    public string QueryKey { get; set; } = string.Empty;

    public DateTimeOffset LastRequestedAt { get; set; }

    public DateTimeOffset? LastRefreshedAt { get; set; }
}
