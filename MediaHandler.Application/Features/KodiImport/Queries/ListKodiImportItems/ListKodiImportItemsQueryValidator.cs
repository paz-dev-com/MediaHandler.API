using FluentValidation;

namespace MediaHandler.Application.Features.KodiImport.Queries.ListKodiImportItems;

public class ListKodiImportItemsQueryValidator : AbstractValidator<ListKodiImportItemsQuery>
{
    public ListKodiImportItemsQueryValidator()
    {
        RuleFor(x => x.RunId).NotEmpty().WithMessage("RunId is required.");
        RuleFor(x => x.Page).GreaterThan(0).WithMessage("Page must be at least 1.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");
        RuleFor(x => x.Outcome)
            .IsInEnum().WithMessage("Outcome is not a valid import item status.")
            .When(x => x.Outcome.HasValue);
        RuleFor(x => x.Kind)
            .IsInEnum().WithMessage("Kind is not a valid Kodi item kind.")
            .When(x => x.Kind.HasValue);
    }
}
