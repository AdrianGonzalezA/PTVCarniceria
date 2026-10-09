using System.Globalization;
using Carnicerias.Api.ExcelImport;
using ClosedXML.Excel;

if (args.Length != 2 || !Directory.Exists(args[0]) || string.IsNullOrWhiteSpace(args[1]))
    throw new ArgumentException("Usage: Carnicerias.ImportFixtures <existing-output-directory> <branch-name>");

var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
var categoryName = $"Importada {suffix}";
var productCode = $"IMP-{suffix}";
var listName = $"Importación {suffix}";
var customerCode = $"CLI-{suffix}";

Save("categories", workbook =>
{
    workbook.Worksheet("Categorias").Cell(2, 1).Value = categoryName;
});
Save("products", workbook =>
{
    var article = workbook.Worksheet("Articulos");
    article.Cell(2, 1).Value = productCode;
    article.Cell(2, 2).Value = "Artículo de prueba importado";
    article.Cell(2, 3).Value = categoryName;
    article.Cell(2, 4).Value = "unit";
    article.Cell(2, 5).Value = "un";
    article.Cell(2, 6).Value = 1000m;
    var code = workbook.Worksheet("CodigosAlternativos");
    code.Cell(2, 1).Value = productCode;
    code.Cell(2, 2).Value = $"ALT-{suffix}";
});
Save("products-reimport", workbook =>
{
    var article = workbook.Worksheet("Articulos");
    article.Cell(2, 1).Value = productCode;
    article.Cell(2, 2).Value = "NO DEBE SOBRESCRIBIRSE";
    article.Cell(2, 3).Value = "Categoría inexistente";
    article.Cell(2, 4).Value = "weight";
    article.Cell(2, 5).Value = "kg";
    article.Cell(2, 6).Value = 9999m;
    var code = workbook.Worksheet("CodigosAlternativos");
    code.Cell(2, 1).Value = productCode;
    code.Cell(2, 2).Value = $"ALT-2-{suffix}";
});
Save("price-lists", workbook =>
{
    workbook.Worksheet("Listas").Cell(2, 1).Value = listName;
    var price = workbook.Worksheet("Precios");
    price.Cell(2, 1).Value = listName;
    price.Cell(2, 2).Value = productCode;
    price.Cell(2, 3).Value = 900m; // Intentionally below statistical cost.
    var branch = workbook.Worksheet("Sucursales");
    branch.Cell(2, 1).Value = listName;
    branch.Cell(2, 2).Value = args[1];
});
Save("price-lists-update", workbook =>
{
    workbook.Worksheet("Listas").Cell(2, 1).Value = listName;
    var price = workbook.Worksheet("Precios");
    price.Cell(2, 1).Value = listName;
    price.Cell(2, 2).Value = productCode;
    price.Cell(2, 3).Value = 800m;
    var branch = workbook.Worksheet("Sucursales");
    branch.Cell(2, 1).Value = listName;
    branch.Cell(2, 2).Value = args[1];
});
Save("customers", workbook =>
{
    var customer = workbook.Worksheet("Clientes");
    customer.Cell(2, 1).Value = customerCode;
    customer.Cell(2, 2).Value = "Cliente importado de prueba";
    customer.Cell(2, 3).Value = "SI";
});

Console.WriteLine($"Categoría: {categoryName}; artículo: {productCode}; lista: {listName}; cliente: {customerCode}");

void Save(string name, Action<XLWorkbook> edit)
{
    var kind = name switch
    {
        "products-reimport" => "products",
        "price-lists-update" => "price-lists",
        _ => name
    };
    using var workbook = new XLWorkbook(new MemoryStream(AdminExcelWorkbook.CreateTemplate(kind)));
    edit(workbook);
    var path = Path.Combine(args[0], $"import-{name}-{suffix}.xlsx");
    workbook.SaveAs(path);
    var parsed = AdminExcelWorkbook.Read(kind, File.ReadAllBytes(path));
    if (parsed.Issues.Count > 0) throw new InvalidDataException($"Fixture {name} inválido.");
    Console.WriteLine(path);
}
