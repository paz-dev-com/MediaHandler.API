using FluentValidation;

namespace MediaHandler.Application.Features.Media.Commands.UpdateMediaRootFolder;

public class UpdateMediaRootFolderCommandValidator : AbstractValidator<UpdateMediaRootFolderCommand>
{
    public UpdateMediaRootFolderCommandValidator()
    {
        RuleFor(x => x.MediaId).NotEmpty();
        RuleFor(x => x.RootFolder)
            .MaximumLength(4096)
            .When(x => x.RootFolder is not null);
    }
}

