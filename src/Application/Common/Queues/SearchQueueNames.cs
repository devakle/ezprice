namespace EZPrice.Application.Common.Queues;

public static class SearchQueueNames
{
    public static string ForSource(string source) => $"search.{source}";
}
