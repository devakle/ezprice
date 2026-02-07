using EZPrice.Domain.Common;

namespace EZPrice.Domain.Entities;

public class Source : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset? LastSuccessfulScrapeAt { get; set; }

    public DateTimeOffset? LastErrorAt { get; set; }

    public string? LastError { get; set; }
}
