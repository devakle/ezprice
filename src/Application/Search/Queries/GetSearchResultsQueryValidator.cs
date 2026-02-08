using FluentValidation;

namespace EZPrice.Application.Search.Queries;

public class GetSearchResultsQueryValidator : AbstractValidator<GetSearchResultsQuery>
{
    public GetSearchResultsQueryValidator()
    {
        RuleFor(v => v.Query)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(v => v.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(v => v.Sort)
            .IsInEnum();
    }
}
