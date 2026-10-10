using Microsoft.EntityFrameworkCore;
using Zev.Api.Community;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class VotingEndpoints
{
    public static RouteGroupBuilder MapVotingEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/zevs/{zevId:guid}/ballots");

        group.MapGet("", async (Guid zevId, int? page, int? pageSize, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Voting.View", cancellationToken, staffOnly: false)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var manager = await access.HasPermissionAsync(zevId, "Voting.Manage", cancellationToken);
            // Staff with Voting.View (e.g. the building representative) follow every published ballot and its
            // result without owning a unit; casting a vote still requires an owned eligibility.
            var staffViewer = !manager && await access.HasPermissionAsync(zevId, "Voting.View", cancellationToken);
            var query = database.Ballots.AsNoTracking().Where(item => item.ZevId == zevId);
            if (staffViewer) query = query.Where(item => item.Status != BallotStatus.Draft);
            else if (!manager)
            {
                if (access.CurrentUserId is not Guid userId) return Results.NotFound();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                query = query.Where(item => item.Status != BallotStatus.Draft
                    && database.BallotEligibilities.Any(eligibility => eligibility.BallotId == item.Id
                        && database.People.Any(person => person.Id == eligibility.PersonId
                            && person.UserId == userId && person.IsActive
                            && person.UserLinkStatus == UserPersonLinkStatus.Verified)
                        && database.Ownerships.Any(ownership => ownership.ZevId == zevId
                            && ownership.PropertyUnitId == eligibility.PropertyUnitId
                            && ownership.PersonId == eligibility.PersonId && ownership.ValidFrom <= today
                            && (ownership.ValidTo == null || ownership.ValidTo > today))));
            }
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.OpensUtc)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.Title, item.Question, kind = item.Kind.ToString(),
                    mode = item.Mode.ToString(), status = item.Status.ToString(), item.OpensUtc, item.ClosesUtc,
                    item.QuorumPercent, item.ApprovalPercent, item.EligibleWeight, item.CastWeight,
                    item.YesWeight, item.NoWeight, item.AbstainWeight, item.QuorumMet, item.Approved })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapPost("", async (Guid zevId, CreateBallotRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Voting.Create", cancellationToken)
                || access.CurrentUserId is not Guid userId) return Results.NotFound();
            if (!AdminValidation.Required(request.Title, 200) || !AdminValidation.Required(request.Question, 4000)
                || !Enum.TryParse<BallotKind>(request.Kind, true, out var kind) || !Enum.IsDefined(kind)
                || !Enum.TryParse<VotingMode>(request.Mode, true, out var mode) || !Enum.IsDefined(mode)
                || !Enum.TryParse<VotingWeightBasis>(request.WeightBasis, true, out var basis) || !Enum.IsDefined(basis)
                || !Enum.TryParse<ApprovalDenominator>(request.ApprovalDenominator, true, out var denominator)
                || !Enum.IsDefined(denominator) || request.QuorumPercent is < 0m or > 100m
                || request.ApprovalPercent is < 0m or > 100m || request.ClosesUtc <= request.OpensUtc)
                return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var ballot = new Ballot { OrganizationId = organizationId, ZevId = zevId,
                CreatedByUserId = userId, Title = request.Title.Trim(), Question = request.Question.Trim(),
                Kind = kind, Mode = mode, WeightBasis = basis, ApprovalDenominator = denominator,
                QuorumPercent = request.QuorumPercent, ApprovalPercent = request.ApprovalPercent,
                OpensUtc = request.OpensUtc, ClosesUtc = request.ClosesUtc };
            database.Ballots.Add(ballot);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/ballots/{ballot.Id}", new { ballot.Id });
        });

        group.MapPost("/{ballotId:guid}/open", async (Guid zevId, Guid ballotId,
            TenantAccess access, VotingService service, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Voting.Manage", cancellationToken)) return Results.NotFound();
            return await service.OpenAsync(zevId, ballotId, cancellationToken)
                ? Results.Ok(new { status = "Open" }) : Results.Conflict();
        });

        group.MapPost("/{ballotId:guid}/votes", async (Guid zevId, Guid ballotId, CastVoteRequest request,
            TenantAccess access, VotingService service, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Voting.View", cancellationToken, staffOnly: false)
                || access.CurrentUserId is not Guid userId) return Results.NotFound();
            if (!Enum.TryParse<VoteChoice>(request.Choice, true, out var choice) || !Enum.IsDefined(choice))
                return Results.BadRequest();
            return await service.CastAsync(zevId, ballotId, request.PropertyUnitId, userId, choice, cancellationToken)
                ? Results.Ok(new { status = "Recorded" }) : Results.Conflict();
        });

        group.MapPost("/{ballotId:guid}/close", async (Guid zevId, Guid ballotId, TenantAccess access,
            VotingService service, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Voting.Manage", cancellationToken)) return Results.NotFound();
            var result = await service.CloseAsync(zevId, ballotId, cancellationToken);
            return result is null ? Results.Conflict() : Results.Ok(result);
        });

        group.MapGet("/{ballotId:guid}/audit", async (Guid zevId, Guid ballotId, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Voting.Manage", cancellationToken)) return Results.NotFound();
            var ballot = await database.Ballots.AsNoTracking().SingleOrDefaultAsync(item => item.Id == ballotId
                && item.ZevId == zevId && item.Status == BallotStatus.Closed, cancellationToken);
            if (ballot is null) return Results.NotFound();
            var votes = await database.Votes.AsNoTracking().Where(item => item.BallotId == ballotId)
                .OrderBy(item => item.CastUtc).Select(item => new { item.Id, item.PropertyUnitId,
                    item.PersonId, choice = item.Choice.ToString(), item.WeightSnapshot, item.CastUtc })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { ballot.Id, ballot.EligibleWeight, ballot.CastWeight, ballot.YesWeight,
                ballot.NoWeight, ballot.AbstainWeight, ballot.QuorumMet, ballot.Approved, votes });
        });

        return api;
    }
}

public sealed record CreateBallotRequest(string Title, string Question, string Kind, string Mode,
    string WeightBasis, string ApprovalDenominator, decimal QuorumPercent, decimal ApprovalPercent,
    DateTimeOffset OpensUtc, DateTimeOffset ClosesUtc);
public sealed record CastVoteRequest(Guid PropertyUnitId, string Choice);
