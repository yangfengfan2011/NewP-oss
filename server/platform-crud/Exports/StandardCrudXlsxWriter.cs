using System.Globalization;
using System.IO.Compression;
using System.Text;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Exports;

/// <summary>
/// 写出最小 XLSX 工作簿；第一版只生成单 sheet 文本单元格，样式和多 sheet 留给后续受控扩展点。
/// </summary>
public sealed class StandardCrudXlsxWriter
{
    public byte[] Write(
        string sheetName,
        IReadOnlyList<StandardCrudExportColumn> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
            WriteEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
            WriteEntry(archive, "xl/workbook.xml", BuildWorkbookXml(sheetName));
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
            WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(columns, rows));
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string BuildContentTypesXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "</Types>";

    private static string BuildRootRelationshipsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string BuildWorkbookXml(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        "<sheets>" +
        $"<sheet name=\"{EscapeXml(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/>" +
        "</sheets>" +
        "</workbook>";

    private static string BuildWorkbookRelationshipsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "</Relationships>";

    private static string BuildWorksheetXml(
        IReadOnlyList<StandardCrudExportColumn> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        AppendRow(xml, rowIndex: 1, columns.Select(column => column.Title));

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            AppendRow(xml, index + 2, columns.Select(column => ToExportText(row.TryGetValue(column.Field, out var value) ? value : null)));
        }

        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

    private static void AppendRow(StringBuilder xml, int rowIndex, IEnumerable<string> values)
    {
        xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex}\">");
        var columnIndex = 1;
        foreach (var value in values)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{BuildCellReference(columnIndex, rowIndex)}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{EscapeXml(value)}</t></is></c>");
            columnIndex++;
        }

        xml.Append("</row>");
    }

    private static string BuildCellReference(int columnIndex, int rowIndex)
    {
        var letters = new StringBuilder();
        var current = columnIndex;
        while (current > 0)
        {
            current--;
            letters.Insert(0, (char)('A' + current % 26));
            current /= 26;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{letters}{rowIndex}");
    }

    private static string ToExportText(object? value) =>
        value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
}
