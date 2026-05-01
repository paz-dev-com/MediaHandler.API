using FluentAssertions;
using MediaHandler.Application.Features.Files.Commands.ScanAndImportNas;

namespace MediaHandler.Tests.Features.Files;

public class ScanAndImportNasCommandValidatorTests
{
    private readonly ScanAndImportNasCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var command = new ScanAndImportNasCommand("/Movies", "en");

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NullLanguageAndBasePath_Passes()
    {
        var command = new ScanAndImportNasCommand();

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LanguageTooLong_Fails()
    {
        var command = new ScanAndImportNasCommand(Language: new string('x', 11));

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Language");
    }

    [Fact]
    public void Validate_LanguageAtMaxLength_Passes()
    {
        var command = new ScanAndImportNasCommand(Language: new string('x', 10));

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_BasePathTooLong_Fails()
    {
        var command = new ScanAndImportNasCommand(new string('x', 1001));

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "BasePath");
    }

    [Fact]
    public void Validate_BasePathAtMaxLength_Passes()
    {
        var command = new ScanAndImportNasCommand(new string('x', 1000));

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}