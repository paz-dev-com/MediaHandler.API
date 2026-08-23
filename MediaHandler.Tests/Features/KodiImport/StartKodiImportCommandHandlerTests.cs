using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Application.Features.KodiImport.Commands.StartKodiImport;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;
using NSubstitute;

namespace MediaHandler.Tests.Features.KodiImport;

public class StartKodiImportCommandHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create();
    private readonly IKodiVideoDbReader _reader = Substitute.For<IKodiVideoDbReader>();
    private readonly IKodiImportFileStore _fileStore = Substitute.For<IKodiImportFileStore>();
    private readonly IImportRunCoordinator _coordinator = Substitute.For<IImportRunCoordinator>();

    private StartKodiImportCommandHandler CreateHandler()
    {
        return new StartKodiImportCommandHandler(_context, _reader, _fileStore, _coordinator);
    }

    private StartKodiImportCommand ValidCommand(
        KodiImportMode mode = KodiImportMode.Import,
        IReadOnlyList<KodiPathMappingSnapshot>? overrides = null)
    {
        return new StartKodiImportCommand("MyVideos121.db", 1234, new MemoryStream([1, 2, 3]), mode, overrides);
    }

    private void ArrangeValidUpload()
    {
        _fileStore.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StoredUpload("/tmp/kodi-import-test.db", 1234)));
        _reader.ValidateAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(KodiDbValidationResult.Valid());
        _coordinator.StartAsync(Arg.Any<KodiImportStartParameters>(), Arg.Any<CancellationToken>())
            .Returns(ci => new KodiImportRunHandle(ci.Arg<KodiImportStartParameters>().ImportRunId));
    }

    [Fact]
    public void StartImport_EmptyUpload_ValidatorRejects()
    {
        var validator = new StartKodiImportCommandValidator();
        var command = new StartKodiImportCommand("MyVideos121.db", 0, Stream.Null, KodiImportMode.Import, null);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DeclaredLengthBytes");
    }

    [Fact]
    public async Task StartImport_UnrecognizedFileName_ReturnsInvalidFileName()
    {
        var command = new StartKodiImportCommand("movie.db", 100, new MemoryStream([1]), KodiImportMode.Import, null);

        var result = await CreateHandler().Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("INVALID_FILE_NAME*");
        await _fileStore.DidNotReceive().SaveAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartImport_UnsupportedVersion_ReturnsErrorNamingVersion()
    {
        _fileStore.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StoredUpload("/tmp/kodi-import-test.db", 1234)));
        _reader.ValidateAsync(Arg.Any<string>(), 999, Arg.Any<CancellationToken>())
            .Returns(KodiDbValidationResult.Invalid(
                "UNSUPPORTED_VERSION", "Unsupported Kodi database version 999. Supported versions: 119, 121, 131."));

        var command = new StartKodiImportCommand("MyVideos999.db", 1234, new MemoryStream([1, 2, 3]),
            KodiImportMode.Import, null);

        var result = await CreateHandler().Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("UNSUPPORTED_VERSION*999*");
        _fileStore.Received().Delete("/tmp/kodi-import-test.db");
        _context.ImportRuns.Should().BeEmpty();
    }

    [Fact]
    public async Task StartImport_OversizedUpload_ReturnsUploadTooLarge()
    {
        _fileStore.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<StoredUpload>("UPLOAD_TOO_LARGE: The uploaded file exceeds the configured size limit of 100 MB."));

        var result = await CreateHandler().Handle(ValidCommand(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("UPLOAD_TOO_LARGE*");
        await _reader.DidNotReceive().ValidateAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartImport_InvalidDatabase_DeletesUploadAndLeavesNoRun()
    {
        _fileStore.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new StoredUpload("/tmp/kodi-import-test.db", 1234)));
        _reader.ValidateAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(KodiDbValidationResult.Invalid("INVALID_KODI_DB", "The uploaded file is not a Kodi video database."));

        var result = await CreateHandler().Handle(ValidCommand(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("INVALID_KODI_DB*");
        _fileStore.Received().Delete("/tmp/kodi-import-test.db");
        _context.ImportRuns.Should().BeEmpty();
        _context.ReviewItems.Should().BeEmpty();
    }

    [Fact]
    public async Task StartImport_RunAlreadyActive_ReturnsImportInProgress()
    {
        ArrangeValidUpload();
        _context.ImportRuns.Add(new ImportRun
        {
            Mode = KodiImportMode.Import,
            Status = ImportRunStatus.Running,
            SourceFileName = "MyVideos121.db",
            SchemaVersion = 121
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler().Handle(ValidCommand(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("IMPORT_IN_PROGRESS*");
        _fileStore.Received().Delete("/tmp/kodi-import-test.db");
        await _coordinator.DidNotReceive().StartAsync(Arg.Any<KodiImportStartParameters>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartImport_CoordinatorRace_ReturnsImportInProgress()
    {
        ArrangeValidUpload();
        _coordinator.StartAsync(Arg.Any<KodiImportStartParameters>(), Arg.Any<CancellationToken>())
            .Returns<Task<KodiImportRunHandle>>(_ => throw new InvalidOperationException("IMPORT_IN_PROGRESS"));

        var result = await CreateHandler().Handle(ValidCommand(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("IMPORT_IN_PROGRESS*");
        _fileStore.Received().Delete("/tmp/kodi-import-test.db");
    }

    [Fact]
    public async Task StartImport_Valid_MergesPersistedMappingsWithOverridesAndStartsCoordinator()
    {
        ArrangeValidUpload();

        _context.KodiPathMappings.AddRange(
            new KodiPathMapping
            {
                KodiPrefix = KodiPathTranslator.NormalizePrefix("smb://FREEBOX/Films/"),
                NasPrefix = "/nas/Movies",
                SortOrder = 0
            },
            new KodiPathMapping
            {
                KodiPrefix = KodiPathTranslator.NormalizePrefix("smb://FREEBOX/Series/"),
                NasPrefix = "/nas/Shows",
                SortOrder = 1
            });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var overrides = new[]
        {
            // Shadows the persisted Films mapping and must win; arrives unnormalized.
            new KodiPathMappingSnapshot("smb://FREEBOX/Films/", "/nas/Override/"),
            new KodiPathMappingSnapshot("smb://FREEBOX/Music/", "/nas/Music/")
        };

        var result = await CreateHandler().Handle(
            ValidCommand(overrides: overrides), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();

        await _coordinator.Received().StartAsync(Arg.Any<KodiImportStartParameters>(), Arg.Any<CancellationToken>());
        var parameters = _coordinator.ReceivedCalls()
            .Select(c => c.GetArguments())
            .Where(a => a[0] is KodiImportStartParameters)
            .Select(a => (KodiImportStartParameters)a[0]!)
            .Single();

        parameters.SchemaVersion.Should().Be(121);
        parameters.SourceFileName.Should().Be("MyVideos121.db");
        parameters.StoredFilePath.Should().Be("/tmp/kodi-import-test.db");
        parameters.Mappings.Should().Equal(
            new[]
            {
                new KodiPathMappingSnapshot("smb://FREEBOX/Films", "/nas/Override"),
                new KodiPathMappingSnapshot("smb://FREEBOX/Music", "/nas/Music"),
                new KodiPathMappingSnapshot("smb://FREEBOX/Series", "/nas/Shows")
            },
            "overrides come first (normalized) and shadow persisted prefixes");
    }

    [Fact]
    public async Task StartImport_PreviewMode_ForwardsPreviewMode()
    {
        ArrangeValidUpload();

        var result = await CreateHandler().Handle(
            ValidCommand(mode: KodiImportMode.Preview), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var parameters = _coordinator.ReceivedCalls()
            .Select(c => c.GetArguments())
            .Where(a => a[0] is KodiImportStartParameters)
            .Select(a => (KodiImportStartParameters)a[0]!)
            .Single();
        parameters.Mode.Should().Be(KodiImportMode.Preview);
    }
}
