namespace EZPrice.Application.Search.Models;

public record SearchSourceStatus(
    string Name,
    string Status,
    int FreshnessSeconds,
    bool HasMore,
    string? NextPageToken);
