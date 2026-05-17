// GetMediaByIdQueryHandlerTests
// Tests T030, T031

using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Features.Media.Queries.GetMediaById;
using MediaHandler.Domain.Enums;
using MediaHandler.Tests.Common;
using NSubstitute;

namespace MediaHandler.Tests.Features.Media;

public class GetMediaByIdQueryHandlerTests
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetMediaByIdQueryHandlerTests()
    {
        _context = TestDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.OktaId.Returns((string?)null);
    }


    private GetMediaByIdQueryHandler CreateHandler() =>
        new(_context, _currentUser);

    [Fact]
    public async Task GetMediaById_EnrichedTvShow_ReturnsStatusAndNumberOfSeasons()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 1234,
            Title = "Breaking Bad",
            Type = MediaType.TvShow,
            Status = "Ended",
            NumberOfSeasons = 5
        };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetMediaByIdQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Ended");
        result.Value.NumberOfSeasons.Should().Be(5);
    }

    [Fact]
    public async Task GetMediaById_UnenrichedMedia_ReturnsBothFieldsAsNull()
    {
        var media = new Domain.Entities.Media
        {
            TmdbId = 5678,
            Title = "Unknown Film",
            Type = MediaType.Film,
            Status = null,
            NumberOfSeasons = null
        };
        _context.Medias.Add(media);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetMediaByIdQuery(media.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().BeNull();
        result.Value.NumberOfSeasons.Should().BeNull();
    }
}

