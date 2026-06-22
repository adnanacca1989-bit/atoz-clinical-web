using ClosedXML.Excel;

namespace AtoZClinical.Web.Services;

public static class ReportExcelService
{
    public static byte[] Export(string sheetName, string[] headers, IEnumerable<object?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);

        for (var c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < headers.Length; c++)
            {
                var value = c < row.Length ? row[c] : null;
                ws.Cell(rowIndex, c + 1).Value = value switch
                {
                    null => "",
                    DateTime dt => dt,
                    decimal d => d,
                    double d => d,
                    int i => i,
                    long l => l,
                    _ => value.ToString() ?? ""
                };
            }
            rowIndex++;
        }

        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
