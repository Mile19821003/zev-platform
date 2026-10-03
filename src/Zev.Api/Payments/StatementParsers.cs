using System.Globalization;
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

public sealed class DelimitedStatementParser(string format, char delimiter) : IBankStatementParser
{
    public string Format => format;

    public IReadOnlyList<ParsedBankTransaction> Parse(string content)
    {
        var rows = Rows(content, delimiter).Where(row => row.Any(field => !string.IsNullOrWhiteSpace(field))).ToList();
        if (rows.Count < 2) throw new FormatException("Statement has no transaction rows.");
        var columns = rows[0].Select((name, index) => (Name: name.Trim().ToLowerInvariant(), Index: index))
            .ToDictionary(item => item.Name, item => item.Index);
        if (!columns.ContainsKey("date") || !columns.ContainsKey("amount"))
            throw new FormatException("Statement requires date and amount columns.");
        string? Value(string[] row, string key) => columns.TryGetValue(key, out var index) && index < row.Length
            ? row[index].Trim() : null;
        var result = new List<ParsedBankTransaction>(rows.Count - 1);
        foreach (var row in rows.Skip(1))
        {
            if (!DateOnly.TryParseExact(Value(row, "date"), ["yyyy-MM-dd", "dd.MM.yyyy", "dd.MM.yyyy."],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(Value(row, "amount"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount) || amount == 0m)
                throw new FormatException("Invalid transaction date or amount.");
            result.Add(new ParsedBankTransaction(date, amount, Value(row, "reference"),
                Value(row, "counterparty"), Value(row, "description"), Value(row, "currency")));
        }
        return result;
    }

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
