using Carnicerias.Infrastructure;

namespace Carnicerias.Api.ExcelImport;

public sealed record ReceiptImportPlan(ImportPreviewResult Preview,
    IReadOnlyList<InventoryReceiptLine> Lines,
    IReadOnlyList<InventoryReceiptPackageRead> PackageReads);

public static class ReceiptImportPlanner
{
    public static ReceiptImportPlan Plan(Guid companyId, Guid receiptId, ParsedImport parsed,
        IReadOnlyList<CatalogProduct> products, IReadOnlyList<ProductCode> alternativeCodes,
        Guid? actorUserId = null, DateTimeOffset? now = null)
    {
        var issues = parsed.Issues.ToList();
        var outcomes = new List<ImportRowOutcome>();
        var lines = new List<InventoryReceiptLine>();
        var packageReads = new List<InventoryReceiptPackageRead>();
        if (parsed.Rows.Count > 500)
            issues.Add(new ImportIssue("Recepciones", 0, "", "TOO_MANY_ROWS",
                "Una recepción admite como máximo 500 renglones."));
        if (issues.Count != 0) return Result();

        var available = products.Where(product => product.CompanyId == companyId && product.IsActive)
            .ToDictionary(product => product.NormalizedCode, StringComparer.Ordinal);
        var byId = products.Where(product => product.CompanyId == companyId && product.IsActive)
            .ToDictionary(product => product.Id);
        foreach (var code in alternativeCodes.Where(code =>
                     code.CompanyId == companyId && code.IsActive &&
                     code.Kind == ProductCodeKind.SaleAlternative))
            if (byId.TryGetValue(code.ProductId, out var product))
                available.TryAdd(code.NormalizedCode, product);
        var packageCodes = alternativeCodes.Where(code => code.CompanyId == companyId &&
                code.IsActive && code.Kind == ProductCodeKind.PurchasePackage)
            .ToDictionary(code => code.NormalizedCode, StringComparer.Ordinal);

        var seen = new HashSet<Guid>();
        foreach (var row in parsed.Rows)
        {
            if (row.Sheet == "Bultos")
            {
                var packageCode = row.Text("CodigoBulto")?.Trim();
                if (string.IsNullOrWhiteSpace(packageCode) || packageCode.Length > 80 ||
                    !packageCodes.TryGetValue(packageCode.ToUpperInvariant(), out var packageDefinition) ||
                    !byId.TryGetValue(packageDefinition.ProductId, out var unitProduct) ||
                    unitProduct.SaleMode != ProductSaleMode.Unit)
                {
                    AddIssue(row, "CodigoBulto", "PACKAGE_CODE_NOT_AVAILABLE",
                        "El código de bulto debe estar activo y pertenecer a un artículo por unidad.");
                    continue;
                }
                var count = row.NumberValue("CantidadBultos");
                var packageExpected = row.NumberValue("CantidadEsperada");
                if (count is null or < 1 or > 100_000 ||
                    count != decimal.Truncate(count.Value) ||
                    count * packageDefinition.UnitsPerPackage!.Value > 100_000 ||
                    packageExpected is < 0 or > 100_000 ||
                    packageExpected.HasValue && packageExpected != decimal.Truncate(packageExpected.Value))
                {
                    AddIssue(row, "CantidadBultos", "INVALID_PACKAGE_QUANTITY",
                        "Indicá una cantidad entera de bultos y hasta 100.000 unidades equivalentes.");
                    continue;
                }
                if (actorUserId is null || actorUserId == Guid.Empty)
                {
                    AddIssue(row, "", "ACTOR_REQUIRED", "La importación requiere un usuario identificado.");
                    continue;
                }
                var units = (int)count.Value * packageDefinition.UnitsPerPackage.Value;
                var packageLine = new InventoryReceiptLine(receiptId, unitProduct, units,
                    packageExpected);
                var packageRead = new InventoryReceiptPackageRead(companyId, receiptId,
                    packageLine.Id, Guid.NewGuid(), packageDefinition, (int)count.Value,
                    actorUserId.Value, now ?? DateTimeOffset.UtcNow);
                lines.Add(packageLine);
                packageReads.Add(packageRead);
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "create",
                    $"{packageDefinition.Code}: {count.Value} × {packageDefinition.UnitsPerPackage.Value} = {units} unidades de {unitProduct.Code}."));
                continue;
            }
            var code = row.Text("CodigoArticulo")?.Trim();
            if (string.IsNullOrWhiteSpace(code) || code.Length > 80)
            {
                AddIssue(row, "CodigoArticulo", "INVALID_CODE",
                    "Ingresá un código de artículo de hasta 80 caracteres.");
                continue;
            }
            if (!available.TryGetValue(code.ToUpperInvariant(), out var product))
            {
                AddIssue(row, "CodigoArticulo", "PRODUCT_NOT_AVAILABLE",
                    "El artículo debe existir y estar activo en la empresa.");
                continue;
            }
            var duplicate = !seen.Add(product.Id);
            if (duplicate)
                AddIssue(row, "CodigoArticulo", "DUPLICATE_ROW",
                    "El artículo está repetido en el archivo.");
            var received = row.NumberValue("CantidadRecibida");
            var expected = row.NumberValue("CantidadEsperada");
            if (received is null or < 0 or > 100_000 ||
                received.HasValue && decimal.Round(received.Value, 3) != received ||
                expected is < 0 or > 100_000 ||
                expected.HasValue && decimal.Round(expected.Value, 3) != expected ||
                received == 0 && expected is null or 0 ||
                product.SaleMode == ProductSaleMode.Unit &&
                    (received != decimal.Truncate(received ?? 0) ||
                     expected.HasValue && expected != decimal.Truncate(expected.Value)))
            {
                AddIssue(row, "CantidadRecibida", "INVALID_PRODUCT_QUANTITY",
                    "Indicá cantidades válidas; las unidades deben ser enteras y los kilos admiten tres decimales.");
                continue;
            }
            if (duplicate) continue;
            lines.Add(new InventoryReceiptLine(receiptId, product, received!.Value, expected));
            outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "create",
                $"{product.Code} · {product.Name}: {received} {product.Unit} recibidos."));
        }
        return Result();

        ReceiptImportPlan Result() => new(new ImportPreviewResult(
            issues.Count == 0 && lines.Count > 0,
            outcomes.Count, 0, 0, 0, outcomes, issues), lines, packageReads);
        void AddIssue(ImportRow row, string column, string code, string message) =>
            issues.Add(new ImportIssue(row.Sheet, row.Number, column, code, message));
    }
}
