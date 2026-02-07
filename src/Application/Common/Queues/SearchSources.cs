namespace EZPrice.Application.Common.Queues;

public static class SearchSources
{
    public const string MercadoLibre = "ml";
    public const string Amazon = "amazon";

    public static readonly string[] All = { MercadoLibre, Amazon };
}
