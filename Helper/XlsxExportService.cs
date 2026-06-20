using SX3_SCANER.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace SX3_SCANER.Helper
{
    internal static class XlsxExportService
    {
        internal static void ExportHistory(string filePath, IEnumerable<HistoryDataRow> rows)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Đường dẫn file Excel trống.", nameof(filePath));

            List<HistoryDataRow> data = (rows ?? Enumerable.Empty<HistoryDataRow>()).ToList();
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using (FileStream stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteTextEntry(archive, "[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types""><Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/><Default Extension=""xml"" ContentType=""application/xml""/><Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/><Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/><Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/><Override PartName=""/docProps/core.xml"" ContentType=""application/vnd.openxmlformats-package.core-properties+xml""/><Override PartName=""/docProps/app.xml"" ContentType=""application/vnd.openxmlformats-officedocument.extended-properties+xml""/></Types>");

                WriteTextEntry(archive, "_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/><Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"" Target=""docProps/core.xml""/><Relationship Id=""rId3"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties"" Target=""docProps/app.xml""/></Relationships>");

                WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/><Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/></Relationships>");

                WriteTextEntry(archive, "xl/workbook.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships""><sheets><sheet name=""ScanHistory"" sheetId=""1"" r:id=""rId1""/></sheets></workbook>");

                WriteTextEntry(archive, "xl/styles.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><fonts count=""2""><font><sz val=""11""/><name val=""Calibri""/></font><font><b/><sz val=""11""/><name val=""Calibri""/></font></fonts><fills count=""2""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill></fills><borders count=""1""><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs><cellXfs count=""2""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/><xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""/></cellXfs><cellStyles count=""1""><cellStyle name=""Normal"" xfId=""0"" builtinId=""0""/></cellStyles></styleSheet>");

                WriteTextEntry(archive, "docProps/app.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Properties xmlns=""http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"" xmlns:vt=""http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes""><Application>SX3 Scanner</Application></Properties>");

                WriteTextEntry(archive, "docProps/core.xml", BuildCoreProperties());
                WriteSheetEntry(archive, data);
            }
        }

        private static string BuildCoreProperties()
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                   "<dc:creator>SX3 Scanner</dc:creator><cp:lastModifiedBy>SX3 Scanner</cp:lastModifiedBy>" +
                   "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:created>" +
                   "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:modified></cp:coreProperties>";
        }

        private static void WriteTextEntry(ZipArchive archive, string path, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static void WriteSheetEntry(ZipArchive archive, List<HistoryDataRow> rows)
        {
            ZipArchiveEntry entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                CloseOutput = true
            };

            using (XmlWriter writer = XmlWriter.Create(entry.Open(), settings))
            {
                writer.WriteStartDocument(true);
                writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

                writer.WriteStartElement("sheetViews");
                writer.WriteStartElement("sheetView");
                writer.WriteAttributeString("workbookViewId", "0");
                writer.WriteStartElement("pane");
                writer.WriteAttributeString("ySplit", "1");
                writer.WriteAttributeString("topLeftCell", "A2");
                writer.WriteAttributeString("activePane", "bottomLeft");
                writer.WriteAttributeString("state", "frozen");
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteStartElement("cols");
                int[] widths = { 8, 22, 18, 20, 22, 12, 12, 38, 12, 32, 16, 16 };

                for (int i = 0; i < widths.Length; i++)
                {
                    writer.WriteStartElement("col");
                    writer.WriteAttributeString("min", (i + 1).ToString());
                    writer.WriteAttributeString("max", (i + 1).ToString());
                    writer.WriteAttributeString("width", widths[i].ToString());
                    writer.WriteAttributeString("customWidth", "1");
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();

                writer.WriteStartElement("sheetData");

                WriteRow(writer, 1, true, new[]
                {
                    "STT",
                    "ScanTime",
                    "BoxName",
                    "ProductPartNumber",
                    "ProductPartName",
                    "SealNo",
                    "LotNo",
                    "ScanData",
                    "Result",
                    "ScanMessage",
                    "ScanWorker",
                    "Loại thùng"
                });

                for (int i = 0; i < rows.Count; i++)
                {
                    HistoryDataRow row = rows[i];

                    WriteRow(writer, i + 2, false, new[]
                    {
                        (i + 1).ToString(),
                        row.ScanTime.HasValue ? row.ScanTime.Value.ToString("dd/MM/yyyy HH:mm:ss") : string.Empty,
                        row.BoxName,
                        row.ProductPartNumber,
                        row.ProductPartName,
                        row.SealNo,
                        row.LotNo,
                        row.ScanData,
                        row.ResultText,
                        row.ScanMessage,
                        row.ScanWorker,
                        row.BoxTypeText
                    });
                }

                writer.WriteEndElement();

                writer.WriteStartElement("autoFilter");
                writer.WriteAttributeString("ref", "A1:L" + Math.Max(1, rows.Count + 1).ToString());
                writer.WriteEndElement();

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        private static void WriteRow(XmlWriter writer, int rowIndex, bool header, string[] values)
        {
            writer.WriteStartElement("row");
            writer.WriteAttributeString("r", rowIndex.ToString());

            for (int i = 0; i < values.Length; i++)
            {
                WriteInlineStringCell(writer, GetCellReference(i + 1, rowIndex), values[i], header);
            }

            writer.WriteEndElement();
        }

        private static void WriteInlineStringCell(XmlWriter writer, string cellReference, string value, bool header)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", cellReference);
            writer.WriteAttributeString("t", "inlineStr");

            if (header)
            {
                writer.WriteAttributeString("s", "1");
            }

            writer.WriteStartElement("is");
            writer.WriteStartElement("t");
            writer.WriteString(value ?? string.Empty);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private static string GetCellReference(int columnIndex, int rowIndex)
        {
            string column = string.Empty;
            int index = columnIndex;

            while (index > 0)
            {
                int remainder = (index - 1) % 26;
                column = (char)('A' + remainder) + column;
                index = (index - 1) / 26;
            }

            return column + rowIndex.ToString();
        }
    }
}