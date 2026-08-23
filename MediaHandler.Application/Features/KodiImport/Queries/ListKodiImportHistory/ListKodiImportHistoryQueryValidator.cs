using FluentValidation;

namespace MediaHandler.Application.Features.KodiImport.Queries.ListKodiImportHistory;

public class ListKodiImportHistoryQueryValidator : AbstractValidator<ListKodiImportHistoryQuery>
{
    public ListKodiImportHistoryQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0).WithMessage("Page must be at least 1.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");
    }
}
