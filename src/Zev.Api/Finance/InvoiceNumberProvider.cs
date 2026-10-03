using Zev.Api.Domain;

namespace Zev.Api.Finance;

public interface IInvoiceNumberProvider
{
    string Format(int year, int sequence);
    string Format(FinancialDocumentSeries series, int year, int sequence);
}

public sealed class InvoiceNumberProvider(IConfiguration? configuration = null) : IInvoiceNumberProvider
{
    public string Format(int year, int sequence)
    {
        var prefix = configuration?["Finance:InvoiceNumberPrefix"]?.Trim().ToUpperInvariant() ?? "INV";
        return FormatCore(prefix, year, sequence);
    }

    public string Format(FinancialDocumentSeries series, int year, int sequence)
    {
        var (key, fallback) = series switch
        {
            FinancialDocumentSeries.Storno => ("Finance:StornoNumberPrefix", "STO"),
            FinancialDocumentSeries.Correction => ("Finance:CorrectionNumberPrefix", "COR"),
            _ => throw new ArgumentOutOfRangeException(nameof(series))
        };
        var prefix = configuration?[key]?.Trim().ToUpperInvariant() ?? fallback;
        var otherPrefix = series == FinancialDocumentSeries.Storno
            ? configuration?["Finance:CorrectionNumberPrefix"]?.Trim().ToUpperInvariant() ?? "COR"
            : configuration?["Finance:StornoNumberPrefix"]?.Trim().ToUpperInvariant() ?? "STO";
        var invoicePrefix = configuration?["Finance:InvoiceNumberPrefix"]?.Trim().ToUpperInvariant() ?? "INV";
        if (prefix == otherPrefix || prefix == invoicePrefix)
            throw new InvalidOperationException("Finance document number prefixes must be distinct.");
        return FormatCore(prefix, year, sequence);
    }

    private static string FormatCore(string prefix, int year, int sequence)
    {
        if (prefix.Length is < 2 or > 12 || !prefix.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("Finance document number prefix must contain 2-12 ASCII letters/digits.");
        if (year is < 1 or > 9999 || sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        return $"{prefix}-{year}-{sequence:000000}";
    }
}
