using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Zev.Api.Payments;

public sealed record ParsedBankTransaction(DateOnly BookingDate, decimal Amount, string? Reference,
    string? Counterparty, string? Description, string? CurrencyCode);

public interface IBankStatementParser
{
    string Format { get; }
    IReadOnlyList<ParsedBankTransaction> Parse(string content);
}

public static class StatementText
{
    // UTF-8 (with or without BOM); exports that are not valid UTF-8 are read as Windows-1250, the usual
    // code page of BiH bank exports.
    public static string Decode(byte[] content)
    {
        var start = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        try { return new UTF8Encoding(false, true).GetString(content, start, content.Length - start); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250).GetString(content);
        }
    }
}

// Delimited bank statements: English (date/amount/reference) or BiH bank headers (Datum/Iznos/Poziv na broj),
// ',', ';' or tab separators, amounts as 1234.56, 1.234,56 or 1,234.56, or separate debit/credit columns.
public sealed class DelimitedStatementParser(string format, char delimiter) : IBankStatementParser
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["date"] = "date", ["datum"] = "date", ["datum knjizenja"] = "date", ["datum valute"] = "date",
        ["datum transakcije"] = "date", ["datum izvrsenja"] = "date", ["booking date"] = "date",
        ["amount"] = "amount", ["iznos"] = "amount", ["iznos km"] = "amount", ["iznos bam"] = "amount",
        ["credit"] = "credit", ["potrazuje"] = "credit", ["uplata"] = "credit", ["priliv"] = "credit", ["odobrenje"] = "credit",
        ["debit"] = "debit", ["duguje"] = "debit", ["isplata"] = "debit", ["odliv"] = "debit", ["zaduzenje"] = "debit",
        ["reference"] = "reference", ["poziv na broj"] = "reference", ["poziv na broj odobrenja"] = "reference",
        ["poziv na broj primaoca"] = "reference", ["referenca"] = "reference", ["broj reference"] = "reference",
        ["counterparty"] = "counterparty", ["uplatilac"] = "counterparty", ["nalogodavac"] = "counterparty",
        ["platilac"] = "counterparty", ["naziv"] = "counterparty", ["naziv uplatioca"] = "counterparty",
        ["description"] = "description", ["opis"] = "description", ["svrha"] = "description",
        ["svrha doznake"] = "description", ["svrha placanja"] = "description", ["svrha uplate"] = "description",
        ["currency"] = "currency", ["valuta"] = "currency"
    };

    public string Format => format;

    public IReadOnlyList<ParsedBankTransaction> Parse(string content)
    {
        content = content.TrimStart('﻿');
        var separator = DetectSeparator(content);
        var rows = Rows(content, separator).Where(row => row.Any(field => !string.IsNullOrWhiteSpace(field))).ToList();
        if (rows.Count < 2) throw new FormatException("Statement has no transaction rows.");
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, index) in rows[0].Select((name, index) => (Normalize(name), index)))
            if (Aliases.TryGetValue(name, out var key)) columns.TryAdd(key, index);
        var signedAmount = columns.ContainsKey("amount");
        if (!columns.ContainsKey("date") || !signedAmount && !columns.ContainsKey("credit") && !columns.ContainsKey("debit"))
            throw new FormatException("Statement requires a date column and an amount (or debit/credit) column.");
        string? Value(string[] row, string key) => columns.TryGetValue(key, out var index) && index < row.Length
            ? row[index].Trim() : null;
        var result = new List<ParsedBankTransaction>(rows.Count - 1);
        foreach (var (row, number) in rows.Skip(1).Select((row, index) => (row, index + 2)))
        {
            if (!DateOnly.TryParseExact(Value(row, "date"), ["yyyy-MM-dd", "dd.MM.yyyy", "dd.MM.yyyy.", "d.M.yyyy",
                    "d.M.yyyy.", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new FormatException($"Row {number}: invalid date '{Value(row, "date")}'.");
            decimal? amount = signedAmount ? ParseAmount(Value(row, "amount"))
                : ParseAmount(Value(row, "credit"), allowEmpty: true) - ParseAmount(Value(row, "debit"), allowEmpty: true);
            if (amount is null or 0m)
                throw new FormatException($"Row {number}: invalid amount '{Value(row, signedAmount ? "amount" : "credit")}'.");
            result.Add(new ParsedBankTransaction(date, amount.Value, Clean(Value(row, "reference")),
                Clean(Value(row, "counterparty")), Clean(Value(row, "description")), Clean(Value(row, "currency"))));
        }
        return result;
    }

    // "1.234,56", "1,234.56", "1234,56", "1234.56", "-30,00". The last '.' or ',' followed by one or two
    // digits is the decimal separator; every other '.', ',', space or apostrophe groups thousands.
    public static decimal? ParseAmount(string? text, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return allowEmpty ? 0m : null;
        var value = text.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty).Replace("'", string.Empty);
        var negative = value.StartsWith('-') || value.StartsWith('(') && value.EndsWith(')');
        value = value.Trim('-', '+', '(', ')');
        var last = value.LastIndexOfAny(['.', ',']);
        var hasDecimals = last >= 0 && value.Length - last - 1 is 1 or 2;
        var integerPart = (hasDecimals ? value[..last] : value).Replace(".", string.Empty).Replace(",", string.Empty);
        var normalized = hasDecimals ? $"{integerPart}.{value[(last + 1)..]}" : integerPart;
        if (normalized.Length == 0 || !normalized.All(character => char.IsAsciiDigit(character) || character == '.')
            || !decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            return null;
        return negative ? -amount : amount;
    }

    private char DetectSeparator(string content)
    {
        var header = content.Split('\n', 2)[0];
        return new[] { delimiter, ';', ',', '\t' }.Distinct()
            .OrderByDescending(candidate => header.Count(character => character == candidate)).First();
    }

    private static string Normalize(string header)
    {
        var decomposed = header.Trim().Trim(':', '.').ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var plain = new string(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray());
        return string.Join(' ', plain.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IEnumerable<string[]> Rows(string content, char delimiter)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '"')
            {
                if (quoted && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"'); index++;
                }
                else quoted = !quoted;
            }
            else if (!quoted && character == delimiter)
            {
                fields.Add(field.ToString()); field.Clear();
            }
            else if (!quoted && character is '\n' or '\r')
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                fields.Add(field.ToString()); field.Clear();
                yield return fields.ToArray(); fields.Clear();
            }
            else field.Append(character);
        }
        if (quoted) throw new FormatException("Unclosed quoted field.");
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }
}

public sealed class XmlStatementParser : IBankStatementParser
{
    public string Format => "XML";

    public IReadOnlyList<ParsedBankTransaction> Parse(string content)
    {
        var document = SafeXml(content);
        var transactions = document.Descendants().Where(item => item.Name.LocalName == "transaction").ToList();
        if (transactions.Count == 0) throw new FormatException("Statement has no transactions.");
        return transactions.Select(item => new ParsedBankTransaction(
            DateOnly.Parse(Child(item, "date") ?? throw new FormatException("Missing date."), CultureInfo.InvariantCulture),
            decimal.Parse(Child(item, "amount") ?? throw new FormatException("Missing amount."), CultureInfo.InvariantCulture),
            Child(item, "reference"), Child(item, "counterparty"), Child(item, "description"),
            Child(item, "currency"))).ToList();
    }

    internal static XDocument SafeXml(string content)
    {
        using var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10_000_000
        });
        return XDocument.Load(reader);
    }

    internal static string? Child(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(item => item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
}

public sealed class Camt053StatementParser : IBankStatementParser
{
    public string Format => "CAMT053";

    public IReadOnlyList<ParsedBankTransaction> Parse(string content)
    {
        var document = XmlStatementParser.SafeXml(content);
        var entries = document.Descendants().Where(item => item.Name.LocalName == "Ntry").ToList();
        if (entries.Count == 0) throw new FormatException("CAMT.053 statement has no entries.");
        var result = new List<ParsedBankTransaction>(entries.Count);
        foreach (var entry in entries)
        {
            var amountText = XmlStatementParser.Child(entry, "Amt");
            var dateText = entry.Descendants().FirstOrDefault(item => item.Name.LocalName == "BookgDt")?
                .Descendants().FirstOrDefault(item => item.Name.LocalName is "Dt" or "DtTm")?.Value;
            if (!decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                || amount <= 0m || !DateOnly.TryParse(dateText?.Length >= 10 ? dateText[..10] : dateText,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new FormatException("Invalid CAMT.053 entry.");
            if (XmlStatementParser.Child(entry, "CdtDbtInd") == "DBIT") amount = -amount;
            var reference = entry.Descendants().FirstOrDefault(item => item.Name.LocalName is "CdtrRefInf")?
                .Descendants().FirstOrDefault(item => item.Name.LocalName == "Ref")?.Value;
            reference ??= entry.Descendants().FirstOrDefault(item => item.Name.LocalName == "EndToEndId")?.Value;
            var counterparty = entry.Descendants().FirstOrDefault(item => item.Name.LocalName is "Dbtr" or "Cdtr")?
                .Descendants().FirstOrDefault(item => item.Name.LocalName == "Nm")?.Value;
            var description = entry.Descendants().FirstOrDefault(item => item.Name.LocalName == "Ustrd")?.Value;
            var currency = entry.Elements().FirstOrDefault(item => item.Name.LocalName == "Amt")?
                .Attributes().FirstOrDefault(item => item.Name.LocalName == "Ccy")?.Value;
            result.Add(new ParsedBankTransaction(date, amount, reference, counterparty, description, currency));
        }
        return result;
    }
}
