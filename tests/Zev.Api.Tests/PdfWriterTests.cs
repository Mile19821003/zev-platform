using System.Text;
using Xunit;
using Zev.Api.Reporting;

namespace Zev.Api.Tests;

public sealed class PdfWriterTests
{
    [Fact]
    public void LongInvoiceSpansMultiplePagesWithoutLosingLines()
    {
        var pdf = Encoding.ASCII.GetString(SimplePdfWriter.Write(Enumerable.Range(1, 65)
            .Select(index => $"Stavka {index}: 1.00").ToArray()));
        Assert.Contains("/Count 3", pdf);
        Assert.Contains(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes("Stavka 1: 1.00")), pdf);
        Assert.Contains(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes("Stavka 65: 1.00")), pdf);
    }
    [Fact]
    public void WritesConsistentCrossReferenceAndEmbeddedUnicodeFont()
    {
        const string names = "Čč Ćć Žž Šš Đđ Đorđe";
        var bytes = SimplePdfWriter.Write(["(Payer)\\", names]);
        var pdf = Encoding.Latin1.GetString(bytes);
        var marker = pdf.LastIndexOf("startxref\n", StringComparison.Ordinal);
        Assert.True(marker > 0);
        var offset = int.Parse(pdf[(marker + "startxref\n".Length)..].Split('\n')[0]);
        Assert.StartsWith("xref\n", pdf[offset..]);
        Assert.Contains("/FontFile2", pdf);
        Assert.Contains("/CIDToGIDMap", pdf);
        Assert.Contains("/ToUnicode", pdf);
        Assert.Contains(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(names)), pdf);
        foreach (var character in names.Where(char.IsLetter).Distinct())
        {
            Assert.Contains($"<{(int)character:X4}> <{(int)character:X4}>", pdf);
            var mapMarker = pdf.IndexOf("/CIDToGIDMap ", StringComparison.Ordinal);
            var rest = pdf[(mapMarker + "/CIDToGIDMap ".Length)..];
            var objectId = int.Parse(rest.Split(' ')[0]);
            var objectStart = pdf.IndexOf($"{objectId} 0 obj\n", StringComparison.Ordinal);
            var streamStart = pdf.IndexOf("stream\n", objectStart, StringComparison.Ordinal) + "stream\n".Length;
            Assert.True((bytes[streamStart + character * 2] << 8 | bytes[streamStart + character * 2 + 1]) > 0);
        }
    }
}
