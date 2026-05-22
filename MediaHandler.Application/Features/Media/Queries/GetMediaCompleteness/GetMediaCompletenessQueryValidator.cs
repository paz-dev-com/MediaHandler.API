using FluentValidation;

namespace MediaHandler.Application.Features.Media.Queries.GetMediaCompleteness;

public class GetMediaCompletenessQueryValidator : AbstractValidator<GetMediaCompletenessQuery>
{
    public GetMediaCompletenessQueryValidator()
    {
        RuleFor(x => x.MediaId).NotEmpty();
    }
}

