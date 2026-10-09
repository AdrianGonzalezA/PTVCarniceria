using System.IO.Compression;
using System.Globalization;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace Carnicerias.Api.ExcelImport;

public sealed record ImportIssue(string Sheet, int Row, string Column, string Code, string Message);

public sealed record ImportCell(string? TextValue, decimal? NumberValue);

public sealed record ImportRow(string Sheet, int Number, IReadOnlyDictionary<string, ImportCell> Cells)
{
    public string? Text(string column) => Cells[column].TextValue;
    public decimal? NumberValue(string column) => Cells[column].NumberValue;
}

public sealed record ParsedImport(IReadOnlyList<ImportRow> Rows, IReadOnlyList<ImportIssue> Issues);

public static class AdminExcelWorkbook
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    private const long MaxExpandedBytes = 30L * 1024 * 1024;
    private const int MaxRowsPerSheet = 2_000;
    private const int MaxZipEntries = 1_000;
    private static readonly Dictionary<string, SheetDefinition[]> Definitions =
        new Dictionary<string, SheetDefinition[]>(StringComparer.Ordinal)
        {
            ["categories"] = [new("Categorias", ["Nombre"], ["Carnes"])],
            ["products"] =
            [
                new("Articulos", ["Codigo", "Nombre", "Categoria", "ModalidadVenta", "Unidad", "CostoARS"],
                    ["2546", "CORTITO C/FALDA EXP", "Carnes", "weight", "kg", "1250.00"]),
                new("CodigosAlternativos", ["CodigoArticulo", "CodigoAlternativo"], ["2546", "02546"])
            ],
            ["price-lists"] =
            [
                new("Listas", ["Nombre"], ["Mostrador"]),
                new("Precios", ["Lista", "CodigoArticulo", "PrecioARS"], ["Mostrador", "2546", "1890.00"]),
                new("Sucursales", ["Lista", "Sucursal"], ["Mostrador", "Sucursal Centro"])
            ],
            ["customers"] = [new("Clientes", ["Codigo", "Nombre", "CuentaCorriente"],
                ["CLI-001", "Cliente de ejemplo", "NO"])]
        };

    public static bool Supports(string kind) => Definitions.ContainsKey(kind);

    public static byte[] CreateTemplate(string kind)
    {
        if (!Definitions.TryGetValue(kind, out var sheets)) throw new ArgumentOutOfRangeException(nameof(kind));
        using var workbook = new XLWorkbook();
        foreach (var definition in sheets)
        {
            var sheet = workbook.Worksheets.Add(definition.Name);
            for (var column = 0; column < definition.Columns.Length; column++)
            {
                var cell = sheet.Cell(1, column + 1);
                cell.Value = definition.Columns[column];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#27383D");
                cell.Style.Font.FontColor = XLColor.White;
                sheet.Column(column + 1).Width = Math.Max(18, definition.Columns[column].Length + 4);
                if (definition.Columns[column] is "CostoARS" or "PrecioARS")
                    sheet.Column(column + 1).Style.NumberFormat.Format = "0.00";
            }
            sheet.SheetView.FreezeRows(1);
        }

        var examples = workbook.Worksheets.Add("Ejemplos");
        examples.Cell(1, 1).Value = "Ejemplos: copiá cada fila a su hoja de carga y reemplazá los valores.";
        examples.Cell(1, 1).Style.Font.Bold = true;
        examples.Column(1).Width = 26;
        var exampleRow = 3;
        foreach (var definition in sheets)
        {
            examples.Cell(exampleRow, 1).Value = definition.Name;
            examples.Cell(exampleRow, 1).Style.Font.Bold = true;
            for (var column = 0; column < definition.Columns.Length; column++)
            {
                examples.Cell(exampleRow + 1, column + 1).Value = definition.Columns[column];
                examples.Cell(exampleRow + 1, column + 1).Style.Font.Bold = true;
                if (definition.Columns[column] is "CostoARS" or "PrecioARS")
                    examples.Cell(exampleRow + 2, column + 1).Value =
                        decimal.Parse(definition.Example[column], CultureInfo.InvariantCulture);
                else examples.Cell(exampleRow + 2, column + 1).Value = definition.Example[column];
                examples.Column(column + 1).Width = Math.Max(examples.Column(column + 1).Width,
                    definition.Columns[column].Length + 4);
            }
            exampleRow += 5;
        }

        var format = workbook.Worksheets.Add("_Formato");
        format.Cell(1, 1).Value = "Tipo";
        format.Cell(1, 2).Value = kind;
        format.Cell(2, 1).Value = "Version";
        format.Cell(2, 2).Value = "1";
        format.Visibility = XLWorksheetVisibility.VeryHidden;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static ParsedImport Read(string kind, byte[] bytes)
    {
        if (!Definitions.TryGetValue(kind, out var sheets)) throw new ArgumentOutOfRangeException(nameof(kind));
        var rows = new List<ImportRow>();
        var issues = new List<ImportIssue>();
        if (bytes.Length == 0 || bytes.Length > MaxFileBytes || !SafeZip(bytes))
        {
            issues.Add(new ImportIssue("", 0, "", "INVALID_XLSX", "El archivo no es un .xlsx válido o seguro."));
            return new ParsedImport(rows, issues);
        }

        try
        {
            using var workbook = new XLWorkbook(new MemoryStream(bytes));
            var format = workbook.Worksheets.FirstOrDefault(sheet => sheet.Name == "_Formato");
            if (format?.Cell(1, 2).GetString() != kind || format.Cell(2, 2).GetString() != "1")
            {
                issues.Add(new ImportIssue("_Formato", 0, "", "TEMPLATE_VERSION",
                    "Descargá la plantilla actual para esta entidad."));
                return new ParsedImport(rows, issues);
            }

            var expectedNames = sheets.Select(sheet => sheet.Name)
                .Concat(["Ejemplos", "_Formato"]).ToHashSet(StringComparer.Ordinal);
            foreach (var unexpected in workbook.Worksheets.Where(sheet => !expectedNames.Contains(sheet.Name)))
                issues.Add(new ImportIssue(unexpected.Name, 0, "", "UNKNOWN_SHEET", "Hoja no reconocida."));

            foreach (var definition in sheets)
            {
                var sheet = workbook.Worksheets.FirstOrDefault(item => item.Name == definition.Name);
                if (sheet is null)
                {
                    issues.Add(new ImportIssue(definition.Name, 1, "", "MISSING_SHEET", "Falta la hoja de carga."));
                    continue;
                }
                ReadSheet(sheet, definition, rows, issues);
            }
            if (workbook.Worksheets.Any(sheet => sheet.CellsUsed().Any(cell => cell.HasFormula)))
                issues.Add(new ImportIssue("", 0, "", "FORMULA_NOT_ALLOWED", "No se admiten fórmulas."));
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or
            IOException or NotSupportedException or OverflowException or FormatException or
            DocumentFormat.OpenXml.Packaging.OpenXmlPackageException)
        {
            issues.Add(new ImportIssue("", 0, "", "INVALID_XLSX", "El archivo no es un .xlsx legible."));
        }
        return new ParsedImport(rows, issues);
    }

    private static void ReadSheet(IXLWorksheet sheet, SheetDefinition definition,
        List<ImportRow> rows, List<ImportIssue> issues)
    {
        var lastColumn = sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var column = 1; column <= lastColumn; column++)
        {
            var name = sheet.Cell(1, column).GetString().Trim();
            if (name.Length == 0 || !definition.Columns.Contains(name, StringComparer.Ordinal))
                issues.Add(new ImportIssue(sheet.Name, 1, name, "UNKNOWN_COLUMN", "Encabezado no reconocido."));
            else if (!columns.TryAdd(name, column))
                issues.Add(new ImportIssue(sheet.Name, 1, name, "DUPLICATE_COLUMN", "Encabezado duplicado."));
        }
        foreach (var missing in definition.Columns.Where(column => !columns.ContainsKey(column)))
            issues.Add(new ImportIssue(sheet.Name, 1, missing, "MISSING_COLUMN", "Falta el encabezado."));
        if (issues.Any(issue => issue.Sheet == sheet.Name && issue.Row == 1)) return;

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        if (lastRow > MaxRowsPerSheet + 1)
        {
            issues.Add(new ImportIssue(sheet.Name, lastRow, "", "TOO_MANY_ROWS",
                "La hoja supera las 2.000 filas de carga."));
            return;
        }
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var cells = new Dictionary<string, ImportCell>(StringComparer.Ordinal);
            var hasValue = false;
            var lastCell = sheet.Row(rowNumber).LastCellUsed();
            if (lastCell is not null && lastCell.Address.ColumnNumber > lastColumn)
                issues.Add(new ImportIssue(sheet.Name, rowNumber, "", "UNKNOWN_COLUMN",
                    "Hay datos fuera de las columnas declaradas."));
            foreach (var column in definition.Columns)
            {
                var cell = sheet.Cell(rowNumber, columns[column]);
                var numeric = column is "CostoARS" or "PrecioARS";
                if (cell.IsEmpty())
                {
                    cells[column] = new ImportCell(null, null);
                    continue;
                }
                hasValue = true;
                if (cell.HasFormula) continue;
                if (numeric && cell.DataType == XLDataType.Number)
                {
                    var value = (decimal)cell.GetDouble();
                    cells[column] = new ImportCell(null, value);
                }
                else if (!numeric && cell.DataType == XLDataType.Text)
                    cells[column] = new ImportCell(cell.GetString().Trim(), null);
                else
                {
                    cells[column] = new ImportCell(null, null);
                    issues.Add(new ImportIssue(sheet.Name, rowNumber, column, "INVALID_CELL_TYPE",
                        numeric ? "Ingresá un importe numérico de Excel." : "Ingresá texto; los códigos deben conservar sus ceros iniciales."));
                }
            }
            if (hasValue && cells.Count == definition.Columns.Length)
                rows.Add(new ImportRow(sheet.Name, rowNumber, cells));
        }
    }

    private static bool SafeZip(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K') return false;
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (archive.Entries.Count is 0 or > MaxZipEntries) return false;
            long expanded = 0;
            var chunk = new byte[81920];
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith('/') || name.Split('/').Contains("..") ||
                    name.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("externalLinks", StringComparison.OrdinalIgnoreCase) ||
                    entry.Length > MaxExpandedBytes - expanded)
                    return false;
                using var input = entry.Open();
                using var buffer = new MemoryStream();
                while (true)
                {
                    var read = input.Read(chunk);
                    if (read == 0) break;
                    expanded += read;
                    if (expanded > MaxExpandedBytes) return false;
                    if (name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                        buffer.Write(chunk, 0, read);
                }
                if (name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                {
                    buffer.Position = 0;
                    if (XDocument.Load(buffer, System.Xml.Linq.LoadOptions.None).Descendants()
                        .Any(element => string.Equals(element.Attribute("TargetMode")?.Value,
                            "External", StringComparison.OrdinalIgnoreCase))) return false;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or
            System.Xml.XmlException) { return false; }
    }

    private sealed record SheetDefinition(string Name, string[] Columns, string[] Example);
}
