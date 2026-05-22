using FluentValidation;

namespace MediaHandler.Application.Features.Media.Queries.GetUnlinkedFiles;

public class GetUnlinkedFilesQueryValidator : AbstractValidator<GetUnlinkedFilesQuery>
{
    public GetUnlinkedFilesQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

