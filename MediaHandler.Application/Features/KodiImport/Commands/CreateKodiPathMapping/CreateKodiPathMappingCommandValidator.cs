using FluentValidation;

namespace MediaHandler.Application.Features.KodiImport.Commands.CreateKodiPathMapping;

public class CreateKodiPathMappingCommandValidator : AbstractValidator<CreateKodiPathMappingCommand>
{
    public CreateKodiPathMappingCommandValidator()
    {
        RuleFor(x => x.KodiPrefix)
            .NotEmpty().WithMessage("KodiPrefix is required.")
            .MaximumLength(500).WithMessage("KodiPrefix must not exceed 500 characters.");

        RuleFor(x => x.NasPrefix)
            .NotEmpty().WithMessage("NasPrefix is required.")
            .MaximumLength(500).WithMessage("NasPrefix must not exceed 500 characters.")
            .Must(p => p.StartsWith('/')).WithMessage("NasPrefix must start with '/'.");

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be zero or greater.")
            .When(x => x.SortOrder.HasValue);
    }
}
