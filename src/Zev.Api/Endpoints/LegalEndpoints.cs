using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;
using Zev.Api.Security;
using Zev.Api.Reporting;
using Zev.Api.Payments;

namespace Zev.Api.Endpoints;

public static class LegalEndpoints
{
    public static RouteGroupBuilder MapLegalEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/zevs/{zevId:guid}/legal");

        group.MapGet("/people", async (Guid zevId, string? search, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Persons.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.People.AsNoTracking().Where(item => database.Ownerships.Any(ownership =>
                ownership.ZevId == zevId && ownership.PersonId == item.Id)
                || database.Occupancies.Any(occupancy => occupancy.ZevId == zevId && occupancy.PersonId == item.Id));
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.DisplayName.Contains(search.Trim()));
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.DisplayName).Skip((currentPage - 1) * currentSize)
                .Take(currentSize).Select(item => new { item.Id, item.DisplayName }).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapGet("/interest-policies", async (Guid zevId, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Legal.View", cancellationToken)) return Results.NotFound();
            var policies = await database.InterestRatePolicies.AsNoTracking().Where(item => item.ZevId == zevId)
                .OrderBy(item => item.ValidFrom).Select(item => new { item.Id, item.ValidFrom, item.ValidTo,
                    item.AnnualRatePercent, item.DaysPerYear, item.IsActive }).ToListAsync(cancellationToken);
            return Results.Ok(policies);
        });

        group.MapPost("/interest-policies", async (Guid zevId, CreateInterestPolicyRequest request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Legal.Edit", cancellationToken)) return Results.NotFound();
            if (request.ValidFrom == default || request.ValidTo <= request.ValidFrom
                || request.AnnualRatePercent is < 0m or > 1000m || request.DaysPerYear is < 1 or > 366)
                return Results.BadRequest();
            var overlaps = await database.InterestRatePolicies.AnyAsync(item => item.ZevId == zevId && item.IsActive
                && (item.ValidTo == null || item.ValidTo > request.ValidFrom)
                && (request.ValidTo == null || item.ValidFrom < request.ValidTo), cancellationToken);
            if (overlaps) return Results.Conflict();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var policy = new InterestRatePolicy
            {
                OrganizationId = organizationId, ZevId = zevId, ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo, AnnualRatePercent = request.AnnualRatePercent,
                DaysPerYear = request.DaysPerYear
            };
            database.InterestRatePolicies.Add(policy);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/legal/interest-policies", new { policy.Id });
        });

        group.MapGet("/reminders", async (Guid zevId, Guid? personId, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Legal.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Reminders.AsNoTracking().Where(item => item.ZevId == zevId);
            if (personId is Guid selectedPerson) query = query.Where(item => item.PersonId == selectedPerson);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.CreatedUtc).Skip((currentPage - 1) * currentSize)
                .Take(currentSize).Select(item => new { item.Id, item.PersonId, personName = item.Person.DisplayName,
                    kind = item.Kind.ToString(), item.AsOf, item.PrincipalSnapshot, item.InterestSnapshot,
                    item.CreatedUtc }).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapGet("/reminders/{reminderId:guid}.pdf", async (Guid zevId, Guid reminderId,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Legal.Documents", cancellationToken)) return Results.NotFound();
            var reminder = await database.Reminders.AsNoTracking().Where(item => item.Id == reminderId
                && item.ZevId == zevId).Select(item => new { item.Id, item.Kind, item.AsOf,
                    item.PrincipalSnapshot, item.InterestSnapshot, person = item.Person.DisplayName })
                .SingleOrDefaultAsync(cancellationToken);
            if (reminder is null) return Results.NotFound();
            var lines = new[] { reminder.Kind == ReminderKind.Payment ? "OPOMENA - INFORMATIVNI PREGLED"
                    : "OPOMENA PRED UTUZENJE - INFORMATIVNI PREGLED",
                $"Lice: {reminder.person}", $"Stanje na dan: {reminder.AsOf:dd.MM.yyyy}",
                $"Otvorena glavnica: {reminder.PrincipalSnapshot:0.00}",
                $"Obracunata kamata: {reminder.InterestSnapshot:0.00}",
                "Valuta historijskog dokumenta nije pohranjena.",
                "Pravni tekst i nacin dostave zahtijevaju odobrenje." };
            return Results.File(SimplePdfWriter.Write(lines), "application/pdf", $"opomena-{reminder.Id:N}.pdf");
        });

        group.MapPost("/reminders", async (Guid zevId, CreateReminderRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Legal.Create", cancellationToken)) return Results.NotFound();
            if (!Enum.TryParse<ReminderKind>(request.Kind, true, out var kind) || !Enum.IsDefined(kind)
                || request.AsOf == default) return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            if (!await database.People.AnyAsync(item => item.Id == request.PersonId
                && item.OrganizationId == organizationId, cancellationToken)) return Results.NotFound();
            var charges = await FinancialReadModel.OpenItems(database, zevId, request.AsOf)
                .Where(item => item.PersonId == request.PersonId && item.DueDate < request.AsOf
                    && item.Amount + item.Correction > item.Allocated)
                .ToListAsync(cancellationToken);
            if (charges.Count == 0) return Results.Conflict(new { error = "No open overdue charges." });
            var currencies = charges.Select(item => item.CurrencyCode)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (currencies.Count != 1 || currencies[0] == FinancialReadModel.UnknownCurrency)
                return Results.Conflict(new { error = "Reminder requires one verified currency." });
            var policies = await database.InterestRatePolicies.AsNoTracking().Where(item => item.ZevId == zevId
                && item.IsActive).ToListAsync(cancellationToken);
            var interest = 0m;
            foreach (var charge in charges)
            {
                var calculated = InterestCalculation.Calculate(charge.Outstanding, charge.DueDate, request.AsOf, policies);
                if (calculated is null) return Results.Conflict(new { error = "Interest policy does not cover the period." });
                interest += calculated.Value;
            }
            var reminder = new Reminder { OrganizationId = organizationId, ZevId = zevId,
                PersonId = request.PersonId, Kind = kind, AsOf = request.AsOf,
                PrincipalSnapshot = charges.Sum(item => item.Outstanding), InterestSnapshot = interest };
            database.Reminders.Add(reminder);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/legal/reminders/{reminder.Id}",
                new { reminder.Id, reminder.PrincipalSnapshot, reminder.InterestSnapshot });
        });

        group.MapGet("/cases", async (Guid zevId, int? page, int? pageSize, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Legal.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.LegalCases.AsNoTracking().Where(item => item.ZevId == zevId);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.FiledOn).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.PersonId, personName = item.Person.DisplayName,
                    item.PrincipalAmount, item.InterestAmount, item.CostsAmount, item.RecoveredAmount,
                    item.Lawyer, item.Court, item.CaseNumber, item.FiledOn, status = item.Status.ToString(),
                    item.Notes, rowVersion = Convert.ToBase64String(item.RowVersion) })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapPost("/cases", async (Guid zevId, CreateLegalCaseRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Legal.Create", cancellationToken)) return Results.NotFound();
            if (!ValidCase(request.PrincipalAmount, request.InterestAmount, request.CostsAmount,
                request.RecoveredAmount, request.Lawyer, request.Court, request.CaseNumber, request.Notes))
                return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            if (!await database.People.AnyAsync(item => item.Id == request.PersonId
                && item.OrganizationId == organizationId, cancellationToken)) return Results.NotFound();
            var legalCase = new LegalCase
            {
                OrganizationId = organizationId, ZevId = zevId, PersonId = request.PersonId,
                PrincipalAmount = request.PrincipalAmount, InterestAmount = request.InterestAmount,
                CostsAmount = request.CostsAmount, RecoveredAmount = request.RecoveredAmount,
                Lawyer = AdminValidation.Clean(request.Lawyer), Court = AdminValidation.Clean(request.Court),
                CaseNumber = AdminValidation.Clean(request.CaseNumber), FiledOn = request.FiledOn,
                Notes = AdminValidation.Clean(request.Notes)
            };
            database.LegalCases.Add(legalCase);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/legal/cases/{legalCase.Id}", new { legalCase.Id });
        });

        group.MapPut("/cases/{caseId:guid}", async (Guid zevId, Guid caseId, UpdateLegalCaseRequest request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Legal.Edit", cancellationToken)) return Results.NotFound();
            if (!ValidCase(request.PrincipalAmount, request.InterestAmount, request.CostsAmount,
                request.RecoveredAmount, request.Lawyer, request.Court, request.CaseNumber, request.Notes)
                || !Enum.TryParse<LegalCaseStatus>(request.Status, true, out var status) || !Enum.IsDefined(status)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var legalCase = await database.LegalCases.SingleOrDefaultAsync(item => item.ZevId == zevId
                && item.Id == caseId, cancellationToken);
            if (legalCase is null) return Results.NotFound();
            database.Entry(legalCase).Property(item => item.RowVersion).OriginalValue = rowVersion;
            legalCase.PrincipalAmount = request.PrincipalAmount;
            legalCase.InterestAmount = request.InterestAmount;
            legalCase.CostsAmount = request.CostsAmount;
            legalCase.RecoveredAmount = request.RecoveredAmount;
            legalCase.Lawyer = AdminValidation.Clean(request.Lawyer);
            legalCase.Court = AdminValidation.Clean(request.Court);
            legalCase.CaseNumber = AdminValidation.Clean(request.CaseNumber);
            legalCase.FiledOn = request.FiledOn;
            legalCase.Notes = AdminValidation.Clean(request.Notes);
            legalCase.Status = status;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { legalCase.Id, status = legalCase.Status.ToString() });
            }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
        });

        group.MapPost("/cases/{caseId:guid}/files", async (Guid zevId, Guid caseId, LinkLegalCaseFileRequest request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Legal.Documents", cancellationToken)) return Results.NotFound();
            var legalCase = await database.LegalCases.AsNoTracking().SingleOrDefaultAsync(item => item.ZevId == zevId
                && item.Id == caseId, cancellationToken);
            if (legalCase is null || !await database.StoredFiles.AnyAsync(item => item.Id == request.FileId
                && item.ZevId == zevId, cancellationToken)) return Results.NotFound();
            var link = new LegalCaseFile { OrganizationId = legalCase.OrganizationId, ZevId = zevId,
                LegalCaseId = caseId, StoredFileId = request.FileId };
            database.LegalCaseFiles.Add(link);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { link.Id });
            }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        return api;
    }

    private static bool ValidCase(decimal principal, decimal interest, decimal costs, decimal recovered,
        string? lawyer, string? court, string? caseNumber, string? notes) =>
        principal >= 0m && interest >= 0m && costs >= 0m && recovered >= 0m
        && AdminValidation.Optional(lawyer, 200) && AdminValidation.Optional(court, 200)
        && AdminValidation.Optional(caseNumber, 100) && AdminValidation.Optional(notes, 2000);
}

public sealed record CreateInterestPolicyRequest(DateOnly ValidFrom, DateOnly? ValidTo,
    decimal AnnualRatePercent, int DaysPerYear);
public sealed record CreateReminderRequest(Guid PersonId, string Kind, DateOnly AsOf);
public sealed record CreateLegalCaseRequest(Guid PersonId, decimal PrincipalAmount, decimal InterestAmount,
    decimal CostsAmount, decimal RecoveredAmount, string? Lawyer, string? Court, string? CaseNumber,
    DateOnly? FiledOn, string? Notes);
public sealed record UpdateLegalCaseRequest(decimal PrincipalAmount, decimal InterestAmount, decimal CostsAmount,
    decimal RecoveredAmount, string? Lawyer, string? Court, string? CaseNumber, DateOnly? FiledOn,
    string Status, string? Notes, string RowVersion);
public sealed record LinkLegalCaseFileRequest(Guid FileId);
