namespace EZPrice.Application.Search.Models;

public record SearchJob(
    string Source,
    string Query,
    string QueryKey,
    int Page,
    DateTimeOffset RequestedAt);
