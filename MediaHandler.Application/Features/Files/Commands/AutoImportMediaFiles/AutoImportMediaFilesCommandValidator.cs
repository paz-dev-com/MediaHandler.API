using FluentValidation;

namespace MediaHandler.Application.Features.Files.Commands.AutoImportMediaFiles;

/// <summary>
/// Validates an <see cref="AutoImportMediaFilesCommand"/> before it is dispatched to the handler.
/// </summary>
public class AutoImportMediaFilesCommandValidator : AbstractValidator<AutoImportMediaFilesCommand>
{
    public AutoImportMediaFilesCommandValidator()
    {
        RuleFor(x => x.Language)
            .MaximumLength(10)
            .When(x => x.Language is not null)
            .WithMessage("Language tag must not exceed 10 characters (e.g., 'en', 'fr', 'zh-Hans').");
    }
}

