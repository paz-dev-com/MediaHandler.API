using FluentValidation;

namespace MediaHandler.Application.Features.Files.Commands.ScanAndImportNas;

/// <summary>
/// Validates a <see cref="ScanAndImportNasCommand"/> before it is dispatched to the handler.
/// </summary>
public class ScanAndImportNasCommandValidator : AbstractValidator<ScanAndImportNasCommand>
{
    public ScanAndImportNasCommandValidator()
    {
        RuleFor(x => x.Language)
            .MaximumLength(10)
            .When(x => x.Language is not null)
            .WithMessage("Language tag must not exceed 10 characters (e.g., 'en', 'fr', 'zh-Hans').");

        RuleFor(x => x.BasePath)
            .MaximumLength(1000)
            .When(x => x.BasePath is not null)
            .WithMessage("BasePath must not exceed 1000 characters.");
    }
}

