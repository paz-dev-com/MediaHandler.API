using FluentValidation;

namespace MediaHandler.Application.Features.Media.Commands.UnlinkMediaFile;

public class UnlinkMediaFileCommandValidator : AbstractValidator<UnlinkMediaFileCommand>
{
    public UnlinkMediaFileCommandValidator()
    {
        RuleFor(x => x.MediaId).NotEmpty();
        RuleFor(x => x.FileId).NotEmpty();
    }
}

