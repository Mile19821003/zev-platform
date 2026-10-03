using System.Buffers.Binary;
using System.Text;

namespace Zev.Api.Reporting;

public static class SimplePdfWriter
{
    private static readonly byte[] FontBytes = LoadFont();
    private static readonly TrueTypeFont Font = new(FontBytes);

    public static byte[] Write(IReadOnlyList<string> lines)
    {
        var wrapped = lines.SelectMany(line => Wrap(line, 72)).ToList();
        var pages = wrapped.Chunk(30).ToArray();
        if (pages.Length == 0) pages = [[]];
        var characters = wrapped.SelectMany(line => line).Distinct().Order().ToArray();
        var fontId = 3 + pages.Length * 2;
        using var stream = new MemoryStream();
        Write(stream, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        WriteObject(stream, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        var pageIds = Enumerable.Range(0, pages.Length).Select(index => 3 + index * 2).ToArray();
        WriteObject(stream, offsets, 2,
            $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pages.Length} >>");
        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var pageId = pageIds[pageIndex];
            WriteObject(stream, offsets, pageId,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {pageId + 1} 0 R >>");
            var content = new StringBuilder("BT /F1 12 Tf 50 780 Td ");
            for (var index = 0; index < pages[pageIndex].Length; index++)
            {
                if (index > 0) content.Append("0 -24 Td ");
                content.Append('<').Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(pages[pageIndex][index])))
                    .Append("> Tj ");
            }
            content.Append("ET");
            WriteStreamObject(stream, offsets, pageId + 1, Encoding.ASCII.GetBytes(content.ToString()));
        }

        WriteObject(stream, offsets, fontId,
            $"<< /Type /Font /Subtype /Type0 /BaseFont /NotoSans-Regular /Encoding /Identity-H /DescendantFonts [{fontId + 1} 0 R] /ToUnicode {fontId + 5} 0 R >>");
        var widths = string.Join(" ", characters.Select(character => $"{(int)character} [{Font.Width(character)}]"));
        WriteObject(stream, offsets, fontId + 1,
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /NotoSans-Regular /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor {fontId + 2} 0 R /CIDToGIDMap {fontId + 4} 0 R /DW 1000 /W [{widths}] >>");
        WriteObject(stream, offsets, fontId + 2,
            $"<< /Type /FontDescriptor /FontName /NotoSans-Regular /Flags 32 /FontBBox [-1000 -1000 2000 2000] /ItalicAngle 0 /Ascent 1069 /Descent -293 /CapHeight 714 /StemV 80 /FontFile2 {fontId + 3} 0 R >>");
        WriteStreamObject(stream, offsets, fontId + 3, FontBytes, $"/Length1 {FontBytes.Length}");
        var glyphMap = new byte[((characters.Length == 0 ? 0 : characters.Max()) + 1) * 2];
        foreach (var character in characters)
            BinaryPrimitives.WriteUInt16BigEndian(glyphMap.AsSpan(character * 2, 2), Font.Glyph(character));
        WriteStreamObject(stream, offsets, fontId + 4, glyphMap);
        var unicode = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var chunk in characters.Chunk(100))
        {
            unicode.Append(chunk.Length).Append(" beginbfchar\n");
            foreach (var character in chunk)
                unicode.Append('<').Append(((int)character).ToString("X4")).Append("> <")
                    .Append(((int)character).ToString("X4")).Append(">\n");
            unicode.Append("endbfchar\n");
        }
        unicode.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");
        WriteStreamObject(stream, offsets, fontId + 5, Encoding.ASCII.GetBytes(unicode.ToString()));
        var xref = stream.Position;
        Write(stream, $"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(stream, $"{offset:0000000000} 00000 n \n");
        Write(stream, $"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return stream.ToArray();
    }

    private static byte[] LoadFont()
    {
        using var resource = typeof(SimplePdfWriter).Assembly.GetManifestResourceStream(
            "Zev.Api.Reporting.Fonts.NotoSans-Regular.ttf")
            ?? throw new InvalidOperationException("Embedded PDF font was not found.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static IEnumerable<string> Wrap(string value, int width)
    {
        for (var position = 0; position < value.Length; position += width)
            yield return value.Substring(position, Math.Min(width, value.Length - position));
        if (value.Length == 0) yield return string.Empty;
    }

    private static void WriteObject(Stream stream, List<long> offsets, int number, string content)
    {
        offsets.Add(stream.Position);
        Write(stream, $"{number} 0 obj\n{content}\nendobj\n");
    }

    private static void WriteStreamObject(Stream stream, List<long> offsets, int number, byte[] data,
        string extra = "")
    {
        offsets.Add(stream.Position);
        Write(stream, $"{number} 0 obj\n<< /Length {data.Length} {extra} >>\nstream\n");
        stream.Write(data);
        Write(stream, "\nendstream\nendobj\n");
    }

    private static void Write(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));

    private sealed class TrueTypeFont
    {
        private readonly byte[] data;
        private readonly int cmap;
        private readonly int hmtx;
        private readonly int horizontalMetrics;
        private readonly int unitsPerEm;

        public TrueTypeFont(byte[] data)
        {
            this.data = data;
            var tables = new Dictionary<string, int>();
            for (var index = 0; index < Read(4); index++)
            {
                var record = 12 + index * 16;
                tables[Encoding.ASCII.GetString(data, record, 4)] = (int)Read32(record + 8);
            }
            var cmapTable = tables["cmap"];
            cmap = Enumerable.Range(0, Read(cmapTable + 2)).Select(index => cmapTable + 4 + index * 8)
                .Where(record => Read(record) is 0 or 3)
                .Select(record => cmapTable + (int)Read32(record + 4))
                .First(offset => Read(offset) == 4);
            hmtx = tables["hmtx"];
            horizontalMetrics = Read(tables["hhea"] + 34);
            unitsPerEm = Read(tables["head"] + 18);
        }

        public ushort Glyph(char character)
        {
            var segments = Read(cmap + 6) / 2;
            var endCodes = cmap + 14;
            var startCodes = endCodes + 2 * segments + 2;
            var deltas = startCodes + 2 * segments;
            var ranges = deltas + 2 * segments;
            for (var segment = 0; segment < segments; segment++)
            {
                if (character > Read(endCodes + 2 * segment)) continue;
                if (character < Read(startCodes + 2 * segment)) break;
                var range = Read(ranges + 2 * segment);
                var glyph = range == 0 ? character : Read(ranges + 2 * segment + range
                    + 2 * (character - Read(startCodes + 2 * segment)));
                return glyph == 0 ? (ushort)0 : (ushort)((glyph + Read(deltas + 2 * segment)) & 0xFFFF);
            }
            return 0;
        }

        public int Width(char character)
        {
            var metric = Math.Min(Glyph(character), horizontalMetrics - 1);
            return (int)Math.Round(Read(hmtx + metric * 4) * 1000m / unitsPerEm);
        }

        private ushort Read(int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        private uint Read32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }
}
