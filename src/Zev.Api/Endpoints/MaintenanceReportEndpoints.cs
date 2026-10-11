using System.Globalization;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Maintenance;
using Zev.Api.Reporting;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

// Reporting centre of the maintenance module: year summary for the screen, PDF and Excel for the ZEV assembly.
public static class MaintenanceReportEndpoints
{
    public static RouteGroupBuilder MapMaintenanceReportEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/zevs/{zevId:guid}/maintenance/reports");

        reports.MapGet("/summary", async (Guid zevId, int? year, TenantAccess access, MaintenanceReportService service,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.View", cancellationToken) is { } denied) return denied;
            var reportYear = year ?? MaintenanceScheduleService.Today().Year;
            if (reportYear is < 2000 or > 2100) return Results.BadRequest();
            return Results.Ok(await service.BuildAsync(zevId, reportYear, cancellationToken));
        });

        reports.MapGet("/{year:int}.xlsx", async (Guid zevId, int year, TenantAccess access, MaintenanceReportService service,
            ZevDbContext database, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Export", cancellationToken) is { } denied) return denied;
            if (year is < 2000 or > 2100) return Results.BadRequest();
            var report = await service.BuildAsync(zevId, year, cancellationToken);
            var rows = new List<IReadOnlyList<object?>>
            {
                new object?[] { $"Održavanje {year}", null, null, null, null, null, null, null, null },
                new object?[] { "Izvor", "Posao", "Kategorija", "Planirano", "Realizovano", "Status", "Izvođač", "Račun", "Iznos (KM)", "Fond" }
            };
            rows.AddRange(report.Rows.Select(item => (IReadOnlyList<object?>)new object?[]
            {
                item.Source, item.Title + (item.Projected ? " (projekcija)" : ""), item.Category, item.PlannedOn?.ToString("dd.MM.yyyy"),
                item.CompletedOn?.ToString("dd.MM.yyyy"), Name(MaintenanceReportService.StatusNames, item.Status), item.Contractor,
                item.InvoiceNumber, item.Cost, item.Fund is null ? null : Name(MaintenanceReportService.FundNames, item.Fund)
            }));
            rows.Add(Array.Empty<object?>());
            rows.Add(new object?[] { "Troškovi po kategorijama", null, null });
            rows.AddRange(report.CostByCategory.Select(item => (IReadOnlyList<object?>)new object?[] { item.Category, item.Jobs, item.Cost }));
            rows.Add(new object?[] { "Ukupno", report.Realized, report.TotalCost });
            await AuditAsync(database, context, access, zevId, year, "xlsx", cancellationToken);
            return Results.File(XlsxWriter.Write(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"odrzavanje-{year}.xlsx");
        });

        reports.MapGet("/{year:int}.pdf", async (Guid zevId, int year, TenantAccess access, MaintenanceReportService service,
            ZevDbContext database, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Export", cancellationToken) is { } denied) return denied;
            if (year is < 2000 or > 2100) return Results.BadRequest();
            var report = await service.BuildAsync(zevId, year, cancellationToken);
            var zev = database.Zevs.Where(item => item.Id == zevId).Select(item => item.Name).Single();
            await AuditAsync(database, context, access, zevId, year, "pdf", cancellationToken);
            return Results.File(SimplePdfWriter.Write(PdfLines(zev, report)), "application/pdf", $"odrzavanje-{year}.pdf");
        });
        return api;
    }

    // Plain lines for the assembly report (the PDF writer wraps long lines and paginates).
    public static List<string> PdfLines(string zevName, MaintenanceReport report)
    {
        var money = (decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("bs-Latn-BA")) + " KM";
        var lines = new List<string>
        {
            $"IZVJEŠTAJ O ODRŽAVANJU {report.Year} - {zevName}", "",
            $"Planirano: {report.Planned}   Realizovano: {report.Realized}   Troškovi: {money(report.TotalCost)}", "",
            "TROŠKOVI PO KATEGORIJAMA"
        };
        lines.AddRange(report.CostByCategory.Count == 0 ? ["- nema evidentiranih troškova"]
            : report.CostByCategory.Select(item => $"- {item.Category}: {money(item.Cost)} ({item.Jobs} posl.)"));
        lines.Add("");
        lines.Add("PO MJESECIMA (planirano / realizovano / trošak)");
        lines.AddRange(report.Months.Where(item => item.Planned + item.Realized > 0)
            .Select(item => $"- {item.Month:00}/{report.Year}: {item.Planned} / {item.Realized} / {money(item.Cost)}"));
        lines.Add("");
        lines.Add("POSLOVI");
        lines.AddRange(report.Rows.Select(item => string.Join(" | ", new[]
        {
            item.PlannedOn?.ToString("dd.MM.yyyy") ?? item.CompletedOn?.ToString("dd.MM.yyyy") ?? "-", item.Title + (item.Projected ? " (projekcija)" : ""),
            item.Category, Name(MaintenanceReportService.StatusNames, item.Status), item.Contractor ?? "-",
            item.Cost is decimal cost ? money(cost) : "-"
        })));
        return lines;
    }

    private static string Name(Dictionary<string, string> names, string key) => names.GetValueOrDefault(key, key);

    private static async Task AuditAsync(ZevDbContext database, HttpContext context, TenantAccess access, Guid zevId, int year,
        string format, CancellationToken cancellationToken)
    {
        var organizationId = database.Zevs.Where(item => item.Id == zevId).Select(item => item.OrganizationId).Single();
        database.AuditLogs.Add(new AuditLog
        {
            ActorUserId = access.CurrentUserId, OrganizationId = organizationId, ZevId = zevId, EntityType = "MaintenanceReport",
            EntityId = year.ToString(), Action = "Export", AfterJson = $"{{\"year\":{year},\"format\":\"{format}\"}}",
            CorrelationId = context.TraceIdentifier
        });
        await database.SaveChangesAsync(cancellationToken);
    }
}
