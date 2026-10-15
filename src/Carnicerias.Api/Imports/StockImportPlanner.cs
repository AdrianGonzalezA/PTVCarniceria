using Carnicerias.Infrastructure;

namespace Carnicerias.Api.ExcelImport;

public sealed record StockImportChange(ImportRow Row, CatalogProduct Product,
    BranchInventoryBalance? Balance, decimal QuantityDelta, decimal OnHandAfter, string Reason);

public sealed record StockImportPlan(IReadOnlyList<StockImportChange> Changes,
    IReadOnlyList<ImportRowOutcome> Outcomes, IReadOnlyList<ImportIssue> Issues);

public static class StockImportPlanner
{
    public static StockImportPlan Plan(Guid companyId, Guid branchId, IReadOnlyList<ImportRow> rows,
        IReadOnlyList<CatalogProduct> products, IReadOnlyList<BranchInventoryBalance> balances)
    {
        var byCode = products.Where(product => product.CompanyId == companyId)
            .ToDictionary(product => product.NormalizedCode, StringComparer.Ordinal);
        var byProduct = balances.Where(balance => balance.CompanyId == companyId &&
                balance.BranchId == branchId)
            .ToDictionary(balance => balance.ProductId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changes = new List<StockImportChange>();
        var outcomes = new List<ImportRowOutcome>();
        var issues = new List<ImportIssue>();

        foreach (var row in rows)
        {
            var code = row.Text("CodigoArticulo");
            var quantity = row.NumberValue("Variacion");
            var description = row.Text("Descripcion");
            if (string.IsNullOrWhiteSpace(code) || code.Length > 80)
            {
                AddIssue(row, "CodigoArticulo", "INVALID_CODE", "Ingresá el código principal del artículo (hasta 80 caracteres).");
                continue;
            }
            var normalized = code.Trim().ToUpperInvariant();
            if (!seen.Add(normalized))
            {
                AddIssue(row, "CodigoArticulo", "DUPLICATE_ROW", "El artículo está repetido en el archivo.");
                continue;
            }
            if (!byCode.TryGetValue(normalized, out var product) || !product.IsActive)
                AddIssue(row, "CodigoArticulo", "PRODUCT_NOT_AVAILABLE", "El artículo debe existir y estar activo en la empresa.");
            if (quantity is null or 0 or > 100000 or < -100000 ||
                decimal.Round(quantity.Value, 3) != quantity.Value)
                AddIssue(row, "Variacion", "INVALID_VARIATION", "Ingresá una variación distinta de cero, entre -100.000 y 100.000, con hasta tres decimales.");
            var reason = string.IsNullOrWhiteSpace(description) ? "Importación Excel" :
                $"Importación Excel: {description.Trim()}";
            if (reason.Length > 240)
                AddIssue(row, "Descripcion", "INVALID_DESCRIPTION", "La descripción es demasiado larga (máximo 221 caracteres).");
            if (product is null || !product.IsActive || quantity is null or 0 or > 100000 or < -100000 ||
                decimal.Round(quantity.Value, 3) != quantity.Value || reason.Length > 240)
                continue;
            if (product.SaleMode == ProductSaleMode.Unit && quantity.Value != decimal.Truncate(quantity.Value))
            {
                AddIssue(row, "Variacion", "INVALID_PRODUCT_QUANTITY", "Este artículo requiere unidades enteras.");
                continue;
            }

            byProduct.TryGetValue(product.Id, out var balance);
            var onHand = balance?.OnHand ?? 0m;
            var reserved = balance?.Reserved ?? 0m;
            var after = onHand + quantity.Value;
            if (after < reserved)
            {
                AddIssue(row, "Variacion", "STOCK_BELOW_RESERVED", "La salida dejaría menos existencia que la reservada.");
                continue;
            }
            if (after > 999_999_999.999m)
            {
                AddIssue(row, "Variacion", "STOCK_QUANTITY_OVERFLOW", "La existencia resultante supera el máximo admitido.");
                continue;
            }
            changes.Add(new StockImportChange(row, product, balance, quantity.Value, after, reason));
            outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "update",
                $"{product.Name}: {onHand} → {after} {product.Unit}."));
        }
        return new StockImportPlan(changes, outcomes, issues);

        void AddIssue(ImportRow row, string column, string code, string message) =>
            issues.Add(new ImportIssue(row.Sheet, row.Number, column, code, message));
    }
}
