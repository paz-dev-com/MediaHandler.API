using AutoMapper;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Features.Auth.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.Application.Features.Auth.Queries.GetCurrentUser;

public record GetCurrentUserQuery : IRequest<Result<UserDto>>;

public class GetCurrentUserQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser, IMapper mapper)
    : IRequestHandler<GetCurrentUserQuery, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.OktaId == currentUser.OktaId, cancellationToken);

        if (user is null)
            return Result.Fail<UserDto>("User not found.");

        return Result.Success(mapper.Map<UserDto>(user));
    }
}