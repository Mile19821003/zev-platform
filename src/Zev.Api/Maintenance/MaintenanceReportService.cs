using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Maintenance;

// One line of the analytical table: a planned occurrence (with its work order, if any) or a work order on its own.
public sealed record MaintenanceReportRow(string Source, string Title, string Category, DateOnly? PlannedOn, DateOnly? CompletedOn,
    string Status, string? Contractor, string? InvoiceNumber, decimal? Cost, string? Fund, bool Projected);

public sealed record MaintenanceCategoryCost(string Category, decimal Cost, int Jobs);

public sealed record MaintenanceMonth(int Month, int Planned, int Realized, decimal Cost);

// One plan on the season timeline: its occurrences in the year with day and status.
public sealed record MaintenanceGanttPlan(Guid PlanId, string Title, string Category, int FrequencyDays, bool IsActive,
    IReadOnlyList<MaintenanceGanttMark> Marks);

public sealed record MaintenanceGanttMark(DateOnly Date, string Status, bool Projected);

public sealed record MaintenanceReport(int Year, IReadOnlyList<MaintenanceReportRow> Rows, IReadOnlyList<MaintenanceCategoryCost> CostByCategory,
    IReadOnlyList<MaintenanceMonth> Months, IReadOnlyList<MaintenanceGanttPlan> Gantt, int Planned, int Realized, decimal TotalCost);

// Year report of the maintenance module: analytical table, costs per category, planned vs realized per month and the
// season timeline. Planned dates beyond the stored 60 days are projected from the plans.
public sealed class MaintenanceReportService(ZevDbContext database, MaintenanceScheduleService schedules)
{
    public async Task<MaintenanceReport> BuildAsync(Guid zevId, int year, CancellationToken cancellationToken)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        var today = MaintenanceScheduleService.Today();
        var occurrences = await database.ScheduledMaintenances.AsNoTracking().Where(item => item.ZevId == zevId
                && item.DueOn >= first && item.DueOn <= last)
            .Select(item => new { item.PlanId, item.DueOn, item.Status, item.WorkOrderId, item.Plan.Title, item.Plan.Category,
                item.Plan.FrequencyDays, item.Plan.IsActive })
            .ToListAsync(cancellationToken);
        var linkedOrderIds = occurrences.Where(item => item.WorkOrderId != null).Select(item => item.WorkOrderId!.Value).ToList();
        var orders = await database.WorkOrders.AsNoTracking().Where(item => item.ZevId == zevId
                && (linkedOrderIds.Contains(item.Id) || (item.CompletedDate >= first && item.CompletedDate <= last)
                    || (item.CompletedDate == null && item.DueOn >= first && item.DueOn <= last)))
            .Select(item => new
            {
                item.Id, item.Title, item.Status, item.DueOn, item.CompletedDate, item.CostAmount, item.InvoiceNumber, item.ExpenseFundKind,
                contractor = item.SupplierId == null ? item.ContractorName
                    : database.Suppliers.IgnoreQueryFilters().Where(supplier => supplier.Id == item.SupplierId).Select(supplier => supplier.Name).FirstOrDefault(),
                ticketCategory = item.TicketId == null ? null
                    : database.MaintenanceTickets.Where(ticket => ticket.Id == item.TicketId).Select(ticket => ticket.Category).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
        var orderById = orders.ToDictionary(item => item.Id);
        var projectedFrom = today.AddDays(MaintenanceScheduleService.HorizonDays + 1) > first
            ? today.AddDays(MaintenanceScheduleService.HorizonDays + 1) : first;
        var projected = await schedules.ProjectAsync(zevId, projectedFrom, last, null, cancellationToken);

        var rows = new List<MaintenanceReportRow>();
        foreach (var item in occurrences)
        {
            var order = item.WorkOrderId is Guid id && orderById.TryGetValue(id, out var linked) ? linked : null;
            rows.Add(new("Plan", item.Title, item.Category, item.DueOn, item.Status == ScheduleStatus.Completed ? order?.CompletedDate ?? item.DueOn : null,
                MaintenanceScheduleService.EffectiveStatus(item.Status, item.DueOn, today), order?.contractor, order?.InvoiceNumber,
                order?.CostAmount, order?.ExpenseFundKind?.ToString(), false));
        }
        rows.AddRange(projected.Select(item => new MaintenanceReportRow("Plan", item.Title, item.Category, item.DueOn, null, "Planned",
            null, null, null, null, true)));
        foreach (var order in orders.Where(item => !linkedOrderIds.Contains(item.Id)))
            rows.Add(new(order.ticketCategory is null ? "Nalog" : "Prijava", order.Title, order.ticketCategory ?? "Ostalo", order.DueOn,
                order.CompletedDate, order.Status.ToString(), order.contractor, order.InvoiceNumber, order.CostAmount,
                order.ExpenseFundKind?.ToString(), false));
        rows = [.. rows.OrderBy(item => item.PlannedOn ?? item.CompletedOn ?? last).ThenBy(item => item.Title)];

        // Costs: completed jobs in the year, by the category of their plan or report.
        var completedJobs = rows.Where(item => item.Cost is > 0m && item.CompletedOn is DateOnly done && done.Year == year).ToList();
        var costByCategory = completedJobs.GroupBy(item => item.Category)
            .Select(group => new MaintenanceCategoryCost(group.Key, group.Sum(item => item.Cost!.Value), group.Count()))
            .OrderByDescending(item => item.Cost).ToList();
        var months = Enumerable.Range(1, 12).Select(month => new MaintenanceMonth(month,
            rows.Count(item => item.Source == "Plan" && item.PlannedOn?.Month == month),
            rows.Count(item => item.CompletedOn is DateOnly done && done.Year == year && done.Month == month),
            completedJobs.Where(item => item.CompletedOn!.Value.Month == month).Sum(item => item.Cost!.Value))).ToList();
        var gantt = occurrences.Select(item => new { item.PlanId, item.Title, item.Category, item.FrequencyDays, item.IsActive })
            .Concat(projected.Select(item => new { item.PlanId, item.Title, item.Category, FrequencyDays = 0, IsActive = true }))
            .GroupBy(item => item.PlanId)
            .Select(group =>
            {
                var plan = group.OrderByDescending(item => item.FrequencyDays).First();
                var marks = occurrences.Where(item => item.PlanId == group.Key)
                    .Select(item => new MaintenanceGanttMark(item.DueOn, MaintenanceScheduleService.EffectiveStatus(item.Status, item.DueOn, today), false))
                    .Concat(projected.Where(item => item.PlanId == group.Key).Select(item => new MaintenanceGanttMark(item.DueOn, "Planned", true)))
                    .OrderBy(item => item.Date).ToList();
                return new MaintenanceGanttPlan(group.Key, plan.Title, plan.Category, plan.FrequencyDays, plan.IsActive, marks);
            })
            .OrderBy(item => item.Category).ThenBy(item => item.Title).ToList();
        return new(year, rows, costByCategory, months, gantt, rows.Count(item => item.Source == "Plan"),
            rows.Count(item => item.CompletedOn is DateOnly done && done.Year == year), completedJobs.Sum(item => item.Cost!.Value));
    }

    public static readonly Dictionary<string, string> StatusNames = new()
    {
        ["Planned"] = "Planirano", ["Due"] = "Dospjelo", ["InProgress"] = "U toku", ["Completed"] = "Završeno",
        ["Cancelled"] = "Otkazano", ["Open"] = "Otvoreno", ["Verified"] = "Potvrđeno"
    };

    public static readonly Dictionary<string, string> FundNames = new() { ["Regular"] = "Redovni", ["Reserve"] = "Rezervni" };
}
