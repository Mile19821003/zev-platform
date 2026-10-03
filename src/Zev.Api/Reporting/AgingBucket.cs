namespace Zev.Api.Reporting;

public static class AgingBucket
{
    public static string For(DateOnly dueDate, DateOnly asOf)
    {
        var days = asOf.DayNumber - dueDate.DayNumber;
        return days switch
        {
            <= 0 => "NotDue",
            <= 30 => "Days1To30",
            <= 60 => "Days31To60",
            <= 90 => "Days61To90",
            <= 180 => "Days91To180",
            <= 365 => "Days181To365",
            _ => "Over365Days"
        };
    }
}
