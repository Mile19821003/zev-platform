using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Zev.Api.Reporting;

public static class XlsxWriter
{
    public static byte[] Write(IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            WritePart(archive, "[Content_Types].xml", writer =>
            {
                writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                writer.WriteStartElement("Default"); writer.WriteAttributeString("Extension", "rels");
                writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml"); writer.WriteEndElement();
                writer.WriteStartElement("Default"); writer.WriteAttributeString("Extension", "xml");
                writer.WriteAttributeString("ContentType", "application/xml"); writer.WriteEndElement();
                writer.WriteStartElement("Override"); writer.WriteAttributeString("PartName", "/xl/workbook.xml");
                writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"); writer.WriteEndElement();
                writer.WriteStartElement("Override"); writer.WriteAttributeString("PartName", "/xl/worksheets/sheet1.xml");
                writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"); writer.WriteEndElement();
                writer.WriteEndElement();
            });
            WritePart(archive, "_rels/.rels", writer =>
            {
                writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                writer.WriteStartElement("Relationship"); writer.WriteAttributeString("Id", "rId1");
                writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
                writer.WriteAttributeString("Target", "xl/workbook.xml"); writer.WriteEndElement(); writer.WriteEndElement();
            });
            WritePart(archive, "xl/workbook.xml", writer =>
            {
                writer.WriteStartElement("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                writer.WriteStartElement("sheets"); writer.WriteStartElement("sheet");
                writer.WriteAttributeString("name", "Izvještaj"); writer.WriteAttributeString("sheetId", "1");
                writer.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId1");
                writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
            });
            WritePart(archive, "xl/_rels/workbook.xml.rels", writer =>
            {
                writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                writer.WriteStartElement("Relationship"); writer.WriteAttributeString("Id", "rId1");
                writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
                writer.WriteAttributeString("Target", "worksheets/sheet1.xml"); writer.WriteEndElement(); writer.WriteEndElement();
            });
            WritePart(archive, "xl/worksheets/sheet1.xml", writer =>
            {
                writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                writer.WriteStartElement("sheetData");
                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    writer.WriteStartElement("row"); writer.WriteAttributeString("r", (rowIndex + 1).ToString(CultureInfo.InvariantCulture));
                    foreach (var value in rows[rowIndex])
                    {
                        writer.WriteStartElement("c");
                        if (value is decimal or int or long)
                        {
                            writer.WriteElementString("v", Convert.ToString(value, CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            writer.WriteAttributeString("t", "inlineStr"); writer.WriteStartElement("is");
                            writer.WriteElementString("t", Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement(); writer.WriteEndElement();
            });
        }
        return memory.ToArray();
    }

    private static void WritePart(ZipArchive archive, string name, Action<XmlWriter> action)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = Encoding.UTF8, CloseOutput = false });
        action(writer);
    }
}
