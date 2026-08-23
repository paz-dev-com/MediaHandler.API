using FluentAssertions;
using MediaHandler.Application.Features.KodiImport.Commands.CreateKodiPathMapping;
using MediaHandler.Application.Features.KodiImport.Commands.DeleteKodiPathMapping;
using MediaHandler.Application.Features.KodiImport.Commands.UpdateKodiPathMapping;
using MediaHandler.Application.Features.KodiImport.Queries.ListKodiPathMappings;
using MediaHandler.Domain.Entities;
using MediaHandler.Tests.Common;

namespace MediaHandler.Tests.Features.KodiImport;

public class KodiPathMappingHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create();

    [Fact]
    public async Task CreateMapping_Valid_PersistsNormalizedAndReturnsDto()
    {
        var handler = new CreateKodiPathMappingCommandHandler(_context);

        var result = await handler.Handle(
            new CreateKodiPathMappingCommand("smb://FREEBOX/Films/", "/nas/Movies/", null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.KodiPrefix.Should().Be("smb://FREEBOX/Films", "trailing slash is trimmed on write");
        result.Value.NasPrefix.Should().Be("/nas/Movies");
        result.Value.SortOrder.Should().Be(0, "first mapping gets the first slot");

        var persisted = _context.KodiPathMappings.Should().ContainSingle().Which;
        persisted.KodiPrefix.Should().Be("smb://FREEBOX/Films");
    }

    [Fact]
    public async Task CreateMapping_DefaultSortOrder_IsMaxPlusOne()
    {
        _context.KodiPathMappings.Add(new KodiPathMapping
        {
            KodiPrefix = "smb://FREEBOX/Films",
            NasPrefix = "/nas/Movies",
            SortOrder = 4
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateKodiPathMappingCommandHandler(_context);
        var result = await handler.Handle(
            new CreateKodiPathMappingCommand("smb://FREEBOX/Series/", "/nas/Shows", null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.SortOrder.Should().Be(5);
    }

    [Fact]
    public async Task CreateMapping_DuplicatePrefix_ReturnsDuplicateMapping()
    {
        _context.KodiPathMappings.Add(new KodiPathMapping
        {
            KodiPrefix = "smb://FREEBOX/Films",
            NasPrefix = "/nas/Movies",
            SortOrder = 0
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateKodiPathMappingCommandHandler(_context);
        var result = await handler.Handle(
            new CreateKodiPathMappingCommand("smb://FREEBOX/Films/", "/nas/Other", null),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("DUPLICATE_MAPPING*");
        _context.KodiPathMappings.Should().HaveCount(1);
    }

    [Fact]
    public async Task UpdateMapping_Existing_UpdatesFields()
    {
        var mapping = new KodiPathMapping
        {
            KodiPrefix = "smb://FREEBOX/Films",
            NasPrefix = "/nas/Movies",
            SortOrder = 0
        };
        _context.KodiPathMappings.Add(mapping);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new UpdateKodiPathMappingCommandHandler(_context);
        var result = await handler.Handle(
            new UpdateKodiPathMappingCommand(mapping.Id, "smb://FREEBOX/Movies/", "/nas/Films", 2),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.KodiPrefix.Should().Be("smb://FREEBOX/Movies");
        result.Value.NasPrefix.Should().Be("/nas/Films");
        result.Value.SortOrder.Should().Be(2);
    }

    [Fact]
    public async Task UpdateMapping_DuplicatePrefixExcludingSelf_ReturnsDuplicateMapping()
    {
        _context.KodiPathMappings.AddRange(
            new KodiPathMapping { KodiPrefix = "smb://FREEBOX/Films", NasPrefix = "/nas/Movies", SortOrder = 0 },
            new KodiPathMapping { KodiPrefix = "smb://FREEBOX/Series", NasPrefix = "/nas/Shows", SortOrder = 1 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var target = _context.KodiPathMappings.First(m => m.KodiPrefix == "smb://FREEBOX/Series");

        var handler = new UpdateKodiPathMappingCommandHandler(_context);
        var result = await handler.Handle(
            new UpdateKodiPathMappingCommand(target.Id, "smb://FREEBOX/Films/", "/nas/Shows", 1),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("DUPLICATE_MAPPING*");
    }

    [Fact]
    public async Task UpdateMapping_Missing_ReturnsNotFound()
    {
        var handler = new UpdateKodiPathMappingCommandHandler(_context);

        var result = await handler.Handle(
            new UpdateKodiPathMappingCommand(Guid.NewGuid(), "smb://x/", "/nas/x", 0),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("NOT_FOUND*");
    }

    [Fact]
    public async Task DeleteMapping_Existing_Removes()
    {
        var mapping = new KodiPathMapping
        {
            KodiPrefix = "smb://FREEBOX/Films",
            NasPrefix = "/nas/Movies",
            SortOrder = 0
        };
        _context.KodiPathMappings.Add(mapping);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DeleteKodiPathMappingCommandHandler(_context);
        var result = await handler.Handle(
            new DeleteKodiPathMappingCommand(mapping.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _context.KodiPathMappings.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMapping_Missing_ReturnsNotFound()
    {
        var handler = new DeleteKodiPathMappingCommandHandler(_context);

        var result = await handler.Handle(
            new DeleteKodiPathMappingCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainMatch("NOT_FOUND*");
    }

    [Fact]
    public async Task ListMappings_ReturnsOrderedBySortOrder()
    {
        _context.KodiPathMappings.AddRange(
            new KodiPathMapping { KodiPrefix = "smb://FREEBOX/Series", NasPrefix = "/nas/Shows", SortOrder = 2 },
            new KodiPathMapping { KodiPrefix = "smb://FREEBOX/Films", NasPrefix = "/nas/Movies", SortOrder = 0 },
            new KodiPathMapping { KodiPrefix = "smb://FREEBOX/Music", NasPrefix = "/nas/Music", SortOrder = 1 });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListKodiPathMappingsQueryHandler(_context);
        var result = await handler.Handle(
            new ListKodiPathMappingsQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(m => m.KodiPrefix).Should().Equal(
            "smb://FREEBOX/Films", "smb://FREEBOX/Music", "smb://FREEBOX/Series");
    }

    [Fact]
    public async Task CreateMapping_NasPrefixNotStartingWithSlash_ValidatorRejects()
    {
        var validator = new CreateKodiPathMappingCommandValidator();

        var result = validator.Validate(new CreateKodiPathMappingCommand("smb://x/", "nas/Movies", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NasPrefix");
    }
}
