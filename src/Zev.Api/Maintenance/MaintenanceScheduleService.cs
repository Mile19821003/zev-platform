using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Maintenance;

public sealed class MaintenanceScheduleService(ZevDbContext database)
{
    // Occurrences are created only this far ahead; the calendar tops the window up as time passes.
    public const int HorizonDays = 60;

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

    // Keeps every active plan of the ZEV filled up to today + HorizonDays (called when the calendar is read).
    public async Task EnsureHorizonAsync(Guid zevId, CancellationToken cancellationToken)
    {
        var plans = await database.MaintenancePlans.AsNoTracking().Where(item => item.ZevId == zevId && item.IsActive)
            .Select(item => item.Id).ToListAsync(cancellationToken);
        foreach (var planId in plans)
            await EnsureUntilAsync(zevId, planId, Today().AddDays(HorizonDays), cancellationToken);
    }
}
