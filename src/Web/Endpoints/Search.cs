using EZPrice.Application.Search.Models;
using EZPrice.Application.Search.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EZPrice.Web.Endpoints;

public class Search : EndpointGroupBase
{
    public override string GroupName => "search";

    public override void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetSearchResults);
    }

    public async Task<Ok<SearchResultsVm>> GetSearchResults(ISender sender, string q, int page = 1)
    {
        var results = await sender.Send(new GetSearchResultsQuery(q, page));
        return TypedResults.Ok(results);
    }
}
