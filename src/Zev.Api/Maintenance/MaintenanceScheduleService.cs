using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Maintenance;

public sealed record CalendarItem(Guid? Id, Guid PlanId, string Title, string Category, DateOnly DueOn, string Status,
    Guid? WorkOrderId, bool Projected);

public sealed class MaintenanceScheduleService(ZevDbContext database)
{
    // Occurrences are created only this far ahead; the calendar tops the window up as time passes.
    public const int HorizonDays = 60;
    public static readonly string[] CalendarStatuses = ["Planned", "Due", "InProgress", "Completed", "Cancelled"];

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    // Stored "Due" means "not done yet": before its date such an occurrence is shown as Planned, from its date on as
    // Due. In progress, completed and cancelled occurrences keep their own status.
    public static string EffectiveStatus(ScheduleStatus status, DateOnly dueOn, DateOnly today) =>
        status == ScheduleStatus.Due && dueOn > today ? "Planned" : status.ToString();

    // First occurrence to create: the latest one on or before today (a single overdue item, not every missed date
    // since an old start), or the plan's start when it is still ahead.
    public static DateOnly WindowStart(DateOnly firstDueOn, int frequencyDays, DateOnly today)
    {
        if (firstDueOn >= today) return firstDueOn;
        var steps = (today.DayNumber - firstDueOn.DayNumber) / frequencyDays;
        return firstDueOn.AddDays(steps * frequencyDays);
    }

    // Creates the missing occurrences from the window start up to `through`, never beyond today + HorizonDays.
    public async Task<int> EnsureUntilAsync(Guid zevId, Guid planId, DateOnly through,
        CancellationToken cancellationToken)
    {
        var today = Today();
        var plan = await database.MaintenancePlans.AsNoTracking().SingleOrDefaultAsync(item => item.Id == planId
            && item.ZevId == zevId && item.IsActive, cancellationToken);
        if (plan is null || plan.FrequencyDays <= 0) return 0;
        var last = through < today.AddDays(HorizonDays) ? through : today.AddDays(HorizonDays);
        var start = WindowStart(plan.FirstDueOn, plan.FrequencyDays, today);
        if (last < start) return 0;
        var existing = (await database.ScheduledMaintenances.AsNoTracking().Where(item => item.PlanId == planId
            && item.DueOn >= start && item.DueOn <= last).Select(item => item.DueOn).ToListAsync(cancellationToken)).ToHashSet();
        var created = 0;
        for (var due = start; due <= last; due = due.AddDays(plan.FrequencyDays))
        {
            if (existing.Contains(due)) continue;
            database.ScheduledMaintenances.Add(new ScheduledMaintenance
            {
                OrganizationId = plan.OrganizationId, ZevId = zevId, PlanId = planId, DueOn = due
            });
            created++;
        }
        if (created == 0) return 0;
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            // A parallel request created the same dates (unique PlanId + DueOn); the window is filled either way.
            database.ChangeTracker.Clear();
            return 0;
        }
        return created;
    }

    public const string RealizedWorkMessage =
        "Plan ima izvršene radne naloge ili troškove. Deaktivirajte ga umjesto brisanja.";

    // Work that must stay in the records: an occurrence that was done or is under way, or one linked to a work order
    // (the order carries the costs).
    public Task<bool> HasRealizedWorkAsync(Guid planId, CancellationToken cancellationToken) =>
        database.ScheduledMaintenances.AnyAsync(item => item.PlanId == planId && (item.WorkOrderId != null
            || item.Status == ScheduleStatus.Completed || item.Status == ScheduleStatus.InProgress), cancellationToken);

    // Occurrences nobody worked on. Removed outright, except (when the plan is only stopped) overdue ones,
    // which are cancelled so the calendar history shows they were not done. Not saved here.
    public async Task<int> ClearOpenOccurrencesAsync(Guid planId, bool cancelOverdue, CancellationToken cancellationToken)
    {
        var today = Today();
        var open = await database.ScheduledMaintenances.Where(item => item.PlanId == planId && item.WorkOrderId == null
            && (item.Status == ScheduleStatus.Due || (!cancelOverdue && item.Status == ScheduleStatus.Cancelled)))
            .ToListAsync(cancellationToken);
        foreach (var item in open)
        {
            if (cancelOverdue && item.DueOn <= today) item.Status = ScheduleStatus.Cancelled;
            else database.ScheduledMaintenances.Remove(item);
        }
        return open.Count;
    }

    // Coming dates of active plans in a period beyond the stored window (e.g. "Cijela godina"): shown as Planned,
    // marked Projected, never stored — they are created when they enter the 60-day window.
    public async Task<IReadOnlyList<CalendarItem>> ProjectAsync(Guid zevId, DateOnly from, DateOnly to, string? category,
        CancellationToken cancellationToken)
    {
        if (to < from) return [];
        var plans = await database.MaintenancePlans.AsNoTracking().Where(item => item.ZevId == zevId && item.IsActive
            && (category == null || item.Category == category)).ToListAsync(cancellationToken);
        var items = new List<CalendarItem>();
        foreach (var plan in plans.Where(item => item.FrequencyDays > 0))
        {
            var start = WindowStart(plan.FirstDueOn, plan.FrequencyDays, from);
            for (var due = start < from ? start.AddDays(plan.FrequencyDays) : start; due <= to; due = due.AddDays(plan.FrequencyDays))
                if (due >= from) items.Add(new CalendarItem(null, plan.Id, plan.Title, plan.Category, due, "Planned", null, true));
        }
        return items;
    }

    // Keeps every active plan of the ZEV filled up to today + HorizonDays (called when the calendar is read).
    public async Task EnsureHorizonAsync(Guid zevId, CancellationToken cancellationToken)
    {
        var plans = await database.MaintenancePlans.AsNoTracking().Where(item => item.ZevId == zevId && item.IsActive)
            .Select(item => item.Id).ToListAsync(cancellationToken);
        foreach (var planId in plans)
            await EnsureUntilAsync(zevId, planId, Today().AddDays(HorizonDays), cancellationToken);
    }
}
