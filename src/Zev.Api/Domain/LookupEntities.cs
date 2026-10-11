namespace Zev.Api.Domain;

// Generic code lists ("šifarnici") for classifications and drop-down options.
public sealed class LookupType
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    // System types are part of the platform: their items can be renamed, reordered or deactivated, never deleted.
    public bool IsSystem { get; set; }
    public List<LookupItem> Items { get; set; } = [];
}

public sealed class LookupItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LookupTypeCode { get; set; } = string.Empty;
    public LookupType LookupType { get; set; } = null!;
    // Stable identifier stored by other records; where an enum exists the code equals its value (e.g. "Urgent").
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    // Null for platform-wide items; set for an item that only one ZEV sees.
    public Guid? ZevId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = LookupSeed.SeededUtc;
    public DateTimeOffset? UpdatedUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public static class LookupSeed
{
    public static readonly DateTimeOffset SeededUtc = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    public static readonly LookupType[] Types =
    [
        new() { Code = "PROPERTY_UNIT_TYPE", Name = "Vrste posebnih dijelova", Description = "Stan, poslovni prostor, garaža…", IsSystem = true },
        new() { Code = "EXPENSE_CATEGORY", Name = "Kategorije rashoda", Description = "Razvrstavanje ulaznih računa i rashoda", IsSystem = true },
        new() { Code = "MAINTENANCE_CATEGORY", Name = "Kategorije kvarova", Description = "Vrsta prijave kvara ili radova", IsSystem = true },
        new() { Code = "MAINTENANCE_PRIORITY", Name = "Prioriteti održavanja", Description = "Hitnost prijave kvara", IsSystem = true },
        new() { Code = "DOCUMENT_CATEGORY", Name = "Kategorije dokumenata", Description = "Vrsta dokumenta zajednice", IsSystem = true },
        new() { Code = "PAYMENT_METHOD", Name = "Načini plaćanja", Description = "Način ručne uplate", IsSystem = true },
        new() { Code = "REVERSAL_REASON", Name = "Razlozi storna", Description = "Ponuđeni razlozi storna i ispravki", IsSystem = true },
    ];

    // (type, code, name, default?) in display order; ids are fixed so the seed is stable across migrations.
    private static readonly (string Type, string Code, string Name, bool Default)[] ItemRows =
    [
        ("PROPERTY_UNIT_TYPE", "Apartment", "Stan", true),
        ("PROPERTY_UNIT_TYPE", "Commercial", "Poslovni prostor", false),
        ("PROPERTY_UNIT_TYPE", "Garage", "Garaža", false),
        ("PROPERTY_UNIT_TYPE", "ParkingSpace", "Parking mjesto", false),
        ("PROPERTY_UNIT_TYPE", "Basement", "Ostava", false),
        ("EXPENSE_CATEGORY", "CURRENT_MAINTENANCE", "Tekuće održavanje", false),
        ("EXPENSE_CATEGORY", "CAPITAL_MAINTENANCE", "Investiciono održavanje", false),
        ("EXPENSE_CATEGORY", "UTILITIES", "Komunalne usluge", false),
        ("EXPENSE_CATEGORY", "ELEVATOR", "Lift", false),
        ("EXPENSE_CATEGORY", "MANAGER", "Upravnik", false),
        ("MAINTENANCE_CATEGORY", "PLUMBING", "Vodovod", false),
        ("MAINTENANCE_CATEGORY", "ELECTRICAL", "Elektroinstalacije", false),
        ("MAINTENANCE_CATEGORY", "CONSTRUCTION", "Građevinski radovi", false),
        ("MAINTENANCE_CATEGORY", "ROOF", "Krov", false),
        ("MAINTENANCE_CATEGORY", "LOCKSMITH", "Bravarija", false),
        ("MAINTENANCE_PRIORITY", "Urgent", "Hitno", false),
        ("MAINTENANCE_PRIORITY", "High", "Visok", false),
        ("MAINTENANCE_PRIORITY", "Normal", "Standardni", true),
        ("MAINTENANCE_PRIORITY", "Low", "Nizak", false),
        ("DOCUMENT_CATEGORY", "Minutes", "Zapisnik", false),
        ("DOCUMENT_CATEGORY", "Contract", "Ugovor", false),
        ("DOCUMENT_CATEGORY", "InsurancePolicy", "Polisa osiguranja", false),
        ("DOCUMENT_CATEGORY", "Decision", "Odluka", false),
        ("DOCUMENT_CATEGORY", "Financial", "Finansijski izvještaj", false),
        ("PAYMENT_METHOD", "Cash", "Gotovina", false),
        ("PAYMENT_METHOD", "Transfer", "Virman", true),
        ("PAYMENT_METHOD", "PaymentSlip", "Uplatnica", false),
        ("REVERSAL_REASON", "WRONG_ENTRY", "Pogrešan unos", false),
        ("REVERSAL_REASON", "DUPLICATE", "Dvostruka uplata", false),
        ("REVERSAL_REASON", "COMPLAINT", "Reklamacija", false),
        ("REVERSAL_REASON", "AMOUNT_CORRECTION", "Ispravak iznosa", false),
        // Added when the forms moved to code lists: values the forms already offered (appended to keep ids stable).
        ("PROPERTY_UNIT_TYPE", "Other", "Ostalo", false),
        ("PAYMENT_METHOD", "Migration", "Migraciona uplata", false),
        ("PAYMENT_METHOD", "Other", "Drugo", false),
        // Document types the community form already offered (PR 9).
        ("DOCUMENT_CATEGORY", "Policy", "Pravilnik", false),
        ("DOCUMENT_CATEGORY", "Statute", "Statut", false),
        ("DOCUMENT_CATEGORY", "Legal", "Pravni dokument", false),
        ("DOCUMENT_CATEGORY", "Other", "Ostalo", false),
    ];

    public static LookupItem[] Items => ItemRows.Select((row, index) => new LookupItem
    {
        Id = new Guid($"5e1f0000-0000-4000-8000-{index + 1:000000000000}"), LookupTypeCode = row.Type, Code = row.Code,
        Name = row.Name, IsDefault = row.Default, IsActive = true, CreatedUtc = SeededUtc,
        SortOrder = 10 * (ItemRows.Take(index).Count(previous => previous.Type == row.Type) + 1)
    }).ToArray();
}
