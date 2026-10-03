using System.IO.Compression;
using System.Xml.Linq;
using Xunit;
using Zev.Api.Reporting;

namespace Zev.Api.Tests;

public sealed class XlsxWriterTests
{
    [Fact]
    public void WritesReadableSpreadsheetWithNumericAndUnicodeCells()
    {
        var bytes = XlsxWriter.Write([new object?[] { "Vlasnik", "Dug" },
            new object?[] { "Đorđe", 12.50m }]);
        using var archive = new ZipArchive(new MemoryStream(bytes));
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheet);
        using var stream = sheet.Open();
        var document = XDocument.Load(stream);
        Assert.Contains("Đorđe", document.ToString());
        Assert.Contains("12.50", document.ToString());
    }
}
