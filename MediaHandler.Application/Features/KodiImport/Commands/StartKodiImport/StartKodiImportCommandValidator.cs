using FluentValidation;

namespace MediaHandler.Application.Features.KodiImport.Commands.StartKodiImport;

public class StartKodiImportCommandValidator : AbstractValidator<StartKodiImportCommand>
{
    public StartKodiImportCommandValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("A file is required.");

        RuleFor(x => x.DeclaredLengthBytes)
            .GreaterThan(0).WithMessage("The uploaded file is empty.");

        RuleFor(x => x.Mode)
            .IsInEnum().WithMessage("Mode must be Import or Preview.");

        RuleForEach(x => x.Overrides)
            .ChildRules(mapping =>
            {
                mapping.RuleFor(m => m.KodiPrefix).NotEmpty().WithMessage("Override KodiPrefix is required.");
                mapping.RuleFor(m => m.NasPrefix).NotEmpty().WithMessage("Override NasPrefix is required.");
            })
            .When(x => x.Overrides is not null);
    }
}
