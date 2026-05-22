using FluentValidation;

namespace MediaHandler.Application.Features.Media.Commands.LinkMediaFile;

public class LinkMediaFileCommandValidator : AbstractValidator<LinkMediaFileCommand>
{
    public LinkMediaFileCommandValidator()
    {
        RuleFor(x => x.MediaId).NotEmpty();
        RuleFor(x => x.FileId).NotEmpty();
    }
}

