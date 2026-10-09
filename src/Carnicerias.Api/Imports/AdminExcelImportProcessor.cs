using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Api.ExcelImport;

public sealed record ImportRowOutcome(string Sheet, int Row, string Status, string Message);

public sealed record ImportPreviewResult(bool CanApply, int CreateCount, int UpdateCount,
    int SkippedCount, int UnchangedCount, IReadOnlyList<ImportRowOutcome> Rows,
    IReadOnlyList<ImportIssue> Issues);

public static class AdminExcelImportProcessor
{
    public static async Task<ImportPreviewResult> AssessAsync(PlatformAccessDbContext db,
        Guid companyId, Guid userId, string kind, ParsedImport parsed, DateTimeOffset now,
        bool apply, CancellationToken cancellationToken)
    {
        var outcomes = new List<ImportRowOutcome>();
        var issues = parsed.Issues.ToList();
        if (issues.Count == 0)
        {
            switch (kind)
            {
                case "categories":
                    await CategoriesAsync(db, companyId, parsed.Rows, outcomes, issues, apply, cancellationToken);
                    break;
                case "customers":
                    await CustomersAsync(db, companyId, parsed.Rows, outcomes, issues, apply, cancellationToken);
                    break;
                case "products":
                    await ProductsAsync(db, companyId, userId, parsed.Rows, outcomes, issues, now,
                        apply, cancellationToken);
                    break;
                case "price-lists":
                    await PricesAsync(db, companyId, userId, parsed.Rows, outcomes, issues, now,
                        apply, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        return new ImportPreviewResult(issues.Count == 0 && parsed.Rows.Count > 0,
            outcomes.Count(row => row.Status == "create"), outcomes.Count(row => row.Status == "update"),
            outcomes.Count(row => row.Status == "skipped"), outcomes.Count(row => row.Status == "unchanged"),
            outcomes, issues);
    }

    private static async Task CategoriesAsync(PlatformAccessDbContext db, Guid companyId,
        IReadOnlyList<ImportRow> rows, List<ImportRowOutcome> outcomes, List<ImportIssue> issues,
        bool apply, CancellationToken cancellationToken)
    {
        var existing = (await db.ProductCategories.AsNoTracking()
            .Where(category => category.CompanyId == companyId)
            .Select(category => category.Name).ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newCategories = new List<ProductCategory>();
        foreach (var row in rows)
        {
            var name = row.Text("Nombre");
            if (!ValidText(name, 120)) AddIssue(issues, row, "Nombre", "INVALID_NAME", "Ingresá un nombre de hasta 120 caracteres.");
            else if (!seen.Add(name!)) AddIssue(issues, row, "Nombre", "DUPLICATE_ROW", "Nombre repetido en el archivo.");
            else if (existing.Contains(name!)) AddIssue(issues, row, "Nombre", "CATEGORY_ALREADY_EXISTS", "La categoría ya existe.");
            else
            {
                newCategories.Add(new ProductCategory(companyId, name!));
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "create", "Crear categoría."));
            }
        }
        if (apply && issues.Count == 0) db.ProductCategories.AddRange(newCategories);
    }

    private static async Task CustomersAsync(PlatformAccessDbContext db, Guid companyId,
        IReadOnlyList<ImportRow> rows, List<ImportRowOutcome> outcomes, List<ImportIssue> issues,
        bool apply, CancellationToken cancellationToken)
    {
        var existing = (await db.CustomerAccounts.AsNoTracking()
            .Where(customer => customer.CompanyId == companyId)
            .Select(customer => customer.NormalizedCode).ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var newCustomers = new List<CustomerAccount>();
        foreach (var row in rows)
        {
            var code = row.Text("Codigo");
            var name = row.Text("Nombre");
            var credit = row.Text("CuentaCorriente");
            if (!ValidText(code, 80)) AddIssue(issues, row, "Codigo", "INVALID_CODE", "Ingresá un código de hasta 80 caracteres.");
            if (!ValidText(name, 200)) AddIssue(issues, row, "Nombre", "INVALID_NAME", "Ingresá un nombre de hasta 200 caracteres.");
            if (credit is not null && credit is not ("SI" or "NO"))
                AddIssue(issues, row, "CuentaCorriente", "INVALID_CREDIT", "Usá SI o NO.");
            if (!ValidText(code, 80) || !ValidText(name, 200) ||
                (credit is not null && credit is not ("SI" or "NO"))) continue;
            var normalized = NormalizeCode(code!);
            if (!seen.Add(normalized)) AddIssue(issues, row, "Codigo", "DUPLICATE_ROW", "Código repetido en el archivo.");
            else if (existing.Contains(normalized))
                AddIssue(issues, row, "Codigo", "CUSTOMER_ALREADY_EXISTS", "El cliente ya existe.");
            else
            {
                var customer = new CustomerAccount(companyId, code!, name!);
                if (credit == "SI") customer.EnableCredit();
                newCustomers.Add(customer);
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "create",
                    credit == "SI" ? "Crear cliente con cuenta corriente habilitada." : "Crear cliente."));
            }
        }
        if (apply && issues.Count == 0) db.CustomerAccounts.AddRange(newCustomers);
    }

    private static async Task ProductsAsync(PlatformAccessDbContext db, Guid companyId, Guid userId,
        IReadOnlyList<ImportRow> rows, List<ImportRowOutcome> outcomes, List<ImportIssue> issues,
        DateTimeOffset now, bool apply, CancellationToken cancellationToken)
    {
        var articleRows = rows.Where(row => row.Sheet == "Articulos").ToArray();
        var codeRows = rows.Where(row => row.Sheet == "CodigosAlternativos").ToArray();
        var categories = await db.ProductCategories.AsNoTracking()
            .Where(category => category.CompanyId == companyId).ToArrayAsync(cancellationToken);
        var categoriesByName = categories.GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var allCodes = articleRows.Select(row => row.Text("Codigo"))
            .Concat(codeRows.Select(row => row.Text("CodigoArticulo")))
            .Concat(codeRows.Select(row => row.Text("CodigoAlternativo")))
            .Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => NormalizeCode(code!))
            .Distinct(StringComparer.Ordinal).ToArray();
        var existingProducts = await db.CatalogProducts.AsNoTracking()
            .Where(product => product.CompanyId == companyId && allCodes.Contains(product.NormalizedCode))
            .ToArrayAsync(cancellationToken);
        var existingAlternates = await db.ProductCodes.AsNoTracking()
            .Where(code => code.CompanyId == companyId && allCodes.Contains(code.NormalizedCode))
            .ToArrayAsync(cancellationToken);
        var productsByCode = existingProducts.ToDictionary(product => product.NormalizedCode, StringComparer.Ordinal);
        var alternatesByCode = existingAlternates.ToDictionary(code => code.NormalizedCode, StringComparer.Ordinal);
        var newProducts = new List<CatalogProduct>();
        var newCodes = new List<ProductCode>();
        var seenMain = new HashSet<string>(StringComparer.Ordinal);
        var seenAlt = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in articleRows)
        {
            var code = row.Text("Codigo");
            if (!ValidText(code, 80))
            {
                AddIssue(issues, row, "Codigo", "INVALID_CODE", "Código de hasta 80 caracteres requerido.");
                continue;
            }
            var normalized = NormalizeCode(code!);
            if (!seenMain.Add(normalized))
            {
                AddIssue(issues, row, "Codigo", "DUPLICATE_ROW", "Código principal repetido.");
                continue;
            }
            if (productsByCode.ContainsKey(normalized))
            {
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "skipped", "No importado: el artículo ya existe."));
                continue;
            }
            var name = row.Text("Nombre");
            var categoryName = row.Text("Categoria");
            var modeText = row.Text("ModalidadVenta");
            var unit = row.Text("Unidad");
            var cost = row.NumberValue("CostoARS");
            if (!ValidText(name, 200)) AddIssue(issues, row, "Nombre", "INVALID_NAME", "Nombre de hasta 200 caracteres requerido.");
            if (!ValidText(unit, 24)) AddIssue(issues, row, "Unidad", "INVALID_UNIT", "Unidad de hasta 24 caracteres requerida.");
            if (modeText is not ("weight" or "unit"))
                AddIssue(issues, row, "ModalidadVenta", "INVALID_MODE", "Usá weight o unit.");
            if (!ValidMoney(cost)) AddIssue(issues, row, "CostoARS", "INVALID_COST", "Costo positivo con hasta dos decimales requerido.");
            if (!ValidText(categoryName, 120) || !categoriesByName.TryGetValue(categoryName!, out var matches) ||
                matches.Length != 1 || !matches[0].IsActive)
                AddIssue(issues, row, "Categoria", "CATEGORY_NOT_ACTIVE", "La categoría debe existir y estar activa en la empresa.");
            if (!ValidText(code, 80) || !ValidText(name, 200) || !ValidText(unit, 24) ||
                modeText is not ("weight" or "unit") || !ValidMoney(cost) ||
                !ValidText(categoryName, 120) || !categoriesByName.TryGetValue(categoryName!, out matches) ||
                matches.Length != 1 || !matches[0].IsActive) continue;
            if (alternatesByCode.ContainsKey(normalized))
                AddIssue(issues, row, "Codigo", "CODE_ALREADY_USED", "El código pertenece a un código alternativo.");
            else
            {
                var product = new CatalogProduct(companyId, matches[0].Id, code!, name!, unit!,
                    modeText == "weight" ? ProductSaleMode.Weight : ProductSaleMode.Unit, cost!.Value);
                productsByCode.Add(normalized, product);
                newProducts.Add(product);
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "create", "Crear artículo y vigencia inicial de costo."));
            }
        }

        foreach (var row in codeRows)
        {
            var articleCode = row.Text("CodigoArticulo");
            var alternateCode = row.Text("CodigoAlternativo");
            if (!ValidText(articleCode, 80)) AddIssue(issues, row, "CodigoArticulo", "INVALID_CODE", "Código principal requerido.");
            if (!ValidText(alternateCode, 80)) AddIssue(issues, row, "CodigoAlternativo", "INVALID_CODE", "Código alternativo requerido.");
            if (!ValidText(articleCode, 80) || !ValidText(alternateCode, 80)) continue;
            var articleKey = NormalizeCode(articleCode!);
            var alternateKey = NormalizeCode(alternateCode!);
            if (!productsByCode.TryGetValue(articleKey, out var product))
                AddIssue(issues, row, "CodigoArticulo", "PRODUCT_NOT_FOUND", "El artículo no existe en esta empresa ni en el archivo.");
            else if (!seenAlt.Add(alternateKey))
                AddIssue(issues, row, "CodigoAlternativo", "DUPLICATE_ROW", "Código alternativo repetido en el archivo.");
            else if (productsByCode.ContainsKey(alternateKey))
                AddIssue(issues, row, "CodigoAlternativo", "CODE_ALREADY_USED", "El código ya es principal de un artículo.");
            else if (alternatesByCode.TryGetValue(alternateKey, out var existing))
            {
                if (existing.ProductId == product.Id)
                    outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "unchanged", "Código ya asociado; se conserva su estado."));
                else AddIssue(issues, row, "CodigoAlternativo", "CODE_ALREADY_USED", "El código pertenece a otro artículo.");
            }
            else
            {
                newCodes.Add(new ProductCode(companyId, product.Id, alternateCode!));
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "create", "Agregar código alternativo."));
            }
        }

        if (apply && issues.Count == 0)
        {
            db.CatalogProducts.AddRange(newProducts);
            db.ProductCostVersions.AddRange(newProducts.Select(product => new ProductCostVersion(
                companyId, product.Id, product.Cost, now, userId)));
            db.ProductCodes.AddRange(newCodes);
        }
    }

    private static async Task PricesAsync(PlatformAccessDbContext db, Guid companyId, Guid userId,
        IReadOnlyList<ImportRow> rows, List<ImportRowOutcome> outcomes, List<ImportIssue> issues,
        DateTimeOffset now, bool apply, CancellationToken cancellationToken)
    {
        var listRows = rows.Where(row => row.Sheet == "Listas").ToArray();
        var priceRows = rows.Where(row => row.Sheet == "Precios").ToArray();
        var branchRows = rows.Where(row => row.Sheet == "Sucursales").ToArray();
        var existingLists = await db.PriceLists.Where(list => list.CompanyId == companyId)
            .ToArrayAsync(cancellationToken);
        var listGroups = existingLists.GroupBy(list => list.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, PriceList>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, group) in listGroups.Where(pair => pair.Value.Length == 1)) lists[name] = group[0];
        var newLists = new List<PriceList>();
        var seenLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in listRows)
        {
            var name = row.Text("Nombre");
            if (!ValidText(name, 160)) AddIssue(issues, row, "Nombre", "INVALID_NAME", "Nombre de hasta 160 caracteres requerido.");
            else if (!seenLists.Add(name!)) AddIssue(issues, row, "Nombre", "DUPLICATE_ROW", "Lista repetida en el archivo.");
            else if (listGroups.TryGetValue(name!, out var group) && group.Length > 1)
                AddIssue(issues, row, "Nombre", "AMBIGUOUS_LIST", "Hay varias listas con este nombre.");
            else if (lists.ContainsKey(name!))
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "unchanged", "Reutilizar lista existente."));
            else
            {
                var list = new PriceList(companyId, name!);
                lists.Add(name!, list);
                newLists.Add(list);
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "create", "Crear lista de precios."));
            }
        }

        var productCodes = priceRows.Select(row => row.Text("CodigoArticulo"))
            .Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => NormalizeCode(code!))
            .Distinct(StringComparer.Ordinal).ToArray();
        var products = await db.CatalogProducts.AsNoTracking()
            .Where(product => product.CompanyId == companyId && productCodes.Contains(product.NormalizedCode))
            .ToDictionaryAsync(product => product.NormalizedCode, cancellationToken);
        var branches = await db.Branches.AsNoTracking().Where(branch => branch.CompanyId == companyId)
            .ToArrayAsync(cancellationToken);
        var branchGroups = branches.GroupBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var listIds = lists.Values.Select(list => list.Id).ToArray();
        var productIds = products.Values.Select(product => product.Id).ToArray();
        var currentPrices = await db.ProductPrices.Where(price => price.CompanyId == companyId &&
            listIds.Contains(price.PriceListId) && productIds.Contains(price.ProductId) &&
            price.EffectiveToUtc == null).ToArrayAsync(cancellationToken);
        var prices = currentPrices.ToDictionary(price => (price.PriceListId, price.ProductId));
        var existingLinks = await db.BranchPriceLists.Where(link => link.CompanyId == companyId &&
            listIds.Contains(link.PriceListId)).ToArrayAsync(cancellationToken);
        var links = existingLinks.ToDictionary(link => (link.PriceListId, link.BranchId));
        var seenPrices = new HashSet<(Guid, Guid)>();
        var seenBranches = new HashSet<(Guid, Guid)>();
        var priceChanges = new List<(PriceList List, CatalogProduct Product, decimal Amount, ProductPrice? Current)>();
        var newLinks = new List<BranchPriceList>();
        var reactivateLinks = new List<BranchPriceList>();

        foreach (var row in priceRows)
        {
            var listName = row.Text("Lista");
            var code = row.Text("CodigoArticulo");
            var amount = row.NumberValue("PrecioARS");
            if (!ValidText(listName, 160) || !lists.TryGetValue(listName!, out var list))
                AddIssue(issues, row, "Lista", "LIST_NOT_FOUND", "La lista debe existir o figurar en la hoja Listas.");
            if (!ValidText(code, 80) || !products.TryGetValue(NormalizeCode(code ?? ""), out var product))
                AddIssue(issues, row, "CodigoArticulo", "PRODUCT_NOT_FOUND", "El artículo debe existir en esta empresa.");
            if (!ValidMoney(amount)) AddIssue(issues, row, "PrecioARS", "INVALID_PRICE", "Precio positivo con hasta dos decimales requerido.");
            if (!ValidText(listName, 160) || !lists.TryGetValue(listName!, out list) ||
                !ValidText(code, 80) || !products.TryGetValue(NormalizeCode(code!), out product) ||
                !ValidMoney(amount)) continue;
            var key = (list.Id, product.Id);
            if (!seenPrices.Add(key))
                AddIssue(issues, row, "CodigoArticulo", "DUPLICATE_ROW", "Precio repetido para esta lista y artículo.");
            else if (prices.TryGetValue(key, out var current) && current.Amount == amount)
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "unchanged", "El precio vigente ya coincide."));
            else if (current is not null && now <= current.EffectiveFromUtc)
                AddIssue(issues, row, "PrecioARS", "PRICE_NOT_YET_EFFECTIVE", "La vigencia actual todavía no puede cerrarse.");
            else
            {
                priceChanges.Add((list, product, amount!.Value, current));
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number,
                    current is null ? "create" : "update", "Abrir nueva vigencia de precio."));
            }
        }

        foreach (var row in branchRows)
        {
            var listName = row.Text("Lista");
            var branchName = row.Text("Sucursal");
            if (!ValidText(listName, 160) || !lists.TryGetValue(listName!, out var list))
                AddIssue(issues, row, "Lista", "LIST_NOT_FOUND", "La lista debe existir o figurar en la hoja Listas.");
            if (!ValidText(branchName, 200) || !branchGroups.TryGetValue(branchName!, out var matches) ||
                matches.Length != 1 || !matches[0].IsActive)
                AddIssue(issues, row, "Sucursal", "BRANCH_NOT_ACTIVE", "La sucursal debe existir y estar activa.");
            if (!ValidText(listName, 160) || !lists.TryGetValue(listName!, out list) ||
                !ValidText(branchName, 200) || !branchGroups.TryGetValue(branchName!, out matches) ||
                matches.Length != 1 || !matches[0].IsActive) continue;
            var key = (list.Id, matches[0].Id);
            if (!seenBranches.Add(key)) AddIssue(issues, row, "Sucursal", "DUPLICATE_ROW", "Asignación repetida.");
            else if (links.TryGetValue(key, out var link) && link.IsActive)
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "unchanged", "La lista ya está habilitada."));
            else if (link is not null)
            {
                reactivateLinks.Add(link);
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "update", "Rehabilitar lista en sucursal."));
            }
            else
            {
                newLinks.Add(new BranchPriceList(companyId, matches[0].Id, list.Id));
                outcomes.Add(new ImportRowOutcome(row.Sheet, row.Number, "create", "Habilitar lista en sucursal."));
            }
        }

        if (apply && issues.Count == 0)
        {
            db.PriceLists.AddRange(newLists);
            db.BranchPriceLists.AddRange(newLinks);
            foreach (var link in reactivateLinks) link.Activate();
            foreach (var (_, _, _, current) in priceChanges)
                current?.CloseAt(now);
            // The partial unique index allows only one open price per list/product.
            await db.SaveChangesAsync(cancellationToken);
            db.ProductPrices.AddRange(priceChanges.Select(change =>
                new ProductPrice(companyId, change.List.Id, change.Product.Id, change.Amount, now, userId)));
        }
    }

    private static bool ValidText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;

    private static bool ValidMoney(decimal? value) =>
        value is > 0 and <= 9_999_999_999.99m && decimal.Round(value.Value, 2) == value.Value;

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static void AddIssue(List<ImportIssue> issues, ImportRow row, string column,
        string code, string message) => issues.Add(new ImportIssue(row.Sheet, row.Number, column, code, message));
}
