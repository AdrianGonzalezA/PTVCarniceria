-- Synthetic, idempotent POS data for the local carnicerias_test_visual database only.
-- This is a development fixture, not a production migration or historical import.
BEGIN;

CREATE TEMP TABLE pos_visual_seed_products (
    category_name text NOT NULL,
    code text PRIMARY KEY,
    product_name text NOT NULL,
    unit_name text NOT NULL,
    sale_mode integer NOT NULL,
    cost_amount numeric(12, 2) NOT NULL,
    sale_amount numeric(12, 2) NOT NULL,
    opening_stock numeric(12, 3) NOT NULL
) ON COMMIT DROP;

INSERT INTO pos_visual_seed_products
    (category_name, code, product_name, unit_name, sale_mode, cost_amount, sale_amount, opening_stock)
VALUES
    ('Vacuno', '1001', 'Bife de chorizo', 'kg', 0, 8900.00, 12900.00, 50.000),
    ('Vacuno', '1002', 'Asado', 'kg', 0, 7900.00, 11500.00, 80.000),
    ('Vacuno', '1003', 'Nalga para milanesa', 'kg', 0, 10300.00, 14800.00, 35.000),
    ('Vacuno', '1004', 'Carne picada especial', 'kg', 0, 6800.00, 9500.00, 25.000),
    ('Cerdo', '2001', 'Bondiola', 'kg', 0, 6200.00, 8900.00, 20.000),
    ('Pollo', '3001', 'Suprema de pollo', 'kg', 0, 6100.00, 8700.00, 30.000),
    ('Pollo', '3002', 'Pata muslo', 'kg', 0, 3500.00, 5100.00, 45.000),
    ('Almacén', '8001', 'Pan rallado 1 kg', 'unidad', 1, 1600.00, 2400.00, 40.000),
    ('Almacén', '9001', 'Gaseosa cola 1,5 L', 'unidad', 1, 1500.00, 2500.00, 35.000);

DO $seed$
DECLARE
    company_id uuid;
    branch_id uuid;
    actor_id uuid;
    price_list_id uuid;
BEGIN
    SELECT "Id" INTO STRICT company_id
    FROM platform_access.companies
    WHERE "Name" = 'Empresa Visual' AND "IsActive";

    SELECT "Id" INTO STRICT branch_id
    FROM platform_access.branches
    WHERE "CompanyId" = company_id AND "Name" = 'Sucursal Visual' AND "IsActive";

    SELECT "Id" INTO STRICT actor_id
    FROM platform_access.users
    WHERE "Username" = 'visual-admin' AND "IsActive";

    INSERT INTO catalog_pricing.categories ("Id", "CompanyId", "Name", "IsActive")
    SELECT md5('carnicerias-pos-visual-category:' || seed.category_name)::uuid,
           company_id, seed.category_name, TRUE
    FROM (SELECT DISTINCT category_name FROM pos_visual_seed_products) AS seed
    ON CONFLICT ("CompanyId", "Name") DO UPDATE SET "IsActive" = TRUE;

    SELECT "Id" INTO price_list_id
    FROM catalog_pricing.price_lists
    WHERE "CompanyId" = company_id AND "Name" = 'Mostrador (datos ficticios)'
    ORDER BY "Id"
    LIMIT 1;

    IF price_list_id IS NULL THEN
        price_list_id := md5('carnicerias-pos-visual-price-list')::uuid;
        INSERT INTO catalog_pricing.price_lists ("Id", "CompanyId", "Name", "IsActive")
        VALUES (price_list_id, company_id, 'Mostrador (datos ficticios)', TRUE);
    ELSE
        UPDATE catalog_pricing.price_lists SET "IsActive" = TRUE WHERE "Id" = price_list_id;
    END IF;

    INSERT INTO catalog_pricing.branch_price_lists ("CompanyId", "BranchId", "PriceListId", "IsActive")
    VALUES (company_id, branch_id, price_list_id, TRUE)
    ON CONFLICT ("BranchId", "PriceListId") DO UPDATE SET "IsActive" = TRUE;

    INSERT INTO catalog_pricing.products
        ("Id", "CompanyId", "CategoryId", "Code", "NormalizedCode", "Name", "Unit", "SaleMode", "Cost", "IsActive")
    SELECT md5('carnicerias-pos-visual-product:' || seed.code)::uuid,
           company_id,
           category."Id",
           seed.code,
           upper(seed.code),
           seed.product_name,
           seed.unit_name,
           seed.sale_mode,
           seed.cost_amount,
           TRUE
    FROM pos_visual_seed_products AS seed
    JOIN catalog_pricing.categories AS category
      ON category."CompanyId" = company_id AND category."Name" = seed.category_name
    ON CONFLICT ("CompanyId", "NormalizedCode") DO NOTHING;

    INSERT INTO catalog_pricing.product_codes ("ProductId", "NormalizedCode", "CompanyId", "Code")
    SELECT product."Id", product."NormalizedCode", company_id, product."Code"
    FROM catalog_pricing.products AS product
    JOIN pos_visual_seed_products AS seed ON seed.code = product."NormalizedCode"
    WHERE product."CompanyId" = company_id
    ON CONFLICT ("ProductId", "NormalizedCode") DO NOTHING;

    INSERT INTO catalog_pricing.product_prices
        ("Id", "CompanyId", "PriceListId", "ProductId", "Amount", "EffectiveFromUtc", "EffectiveToUtc", "ChangedByUserId")
    SELECT md5('carnicerias-pos-visual-price:' || seed.code)::uuid,
           company_id,
           price_list_id,
           product."Id",
           seed.sale_amount,
           clock_timestamp(),
           NULL,
           actor_id
    FROM pos_visual_seed_products AS seed
    JOIN catalog_pricing.products AS product
      ON product."CompanyId" = company_id AND product."NormalizedCode" = upper(seed.code)
    WHERE NOT EXISTS (
        SELECT 1
        FROM catalog_pricing.product_prices AS current_price
        WHERE current_price."CompanyId" = company_id
          AND current_price."PriceListId" = price_list_id
          AND current_price."ProductId" = product."Id"
          AND current_price."EffectiveToUtc" IS NULL
    );

    WITH inserted_balances AS (
        INSERT INTO inventory.branch_inventory ("CompanyId", "BranchId", "ProductId", "OnHand", "Reserved")
        SELECT company_id, branch_id, product."Id", seed.opening_stock, 0
        FROM pos_visual_seed_products AS seed
        JOIN catalog_pricing.products AS product
          ON product."CompanyId" = company_id AND product."NormalizedCode" = upper(seed.code)
        ON CONFLICT ("CompanyId", "BranchId", "ProductId") DO NOTHING
        RETURNING "CompanyId", "BranchId", "ProductId", "OnHand"
    )
    INSERT INTO inventory.inventory_movements
        ("Id", "CompanyId", "BranchId", "ProductId", "UserId", "OperationId", "Kind", "QuantityDelta", "Reason", "CreatedAtUtc")
    SELECT md5('carnicerias-pos-visual-opening-movement:' || seed.code)::uuid,
           inserted."CompanyId",
           inserted."BranchId",
           inserted."ProductId",
           actor_id,
           md5('carnicerias-pos-visual-opening-operation:' || seed.code)::uuid,
           0,
           inserted."OnHand",
           'Carga inicial ficticia para pruebas del punto de venta',
           clock_timestamp()
    FROM inserted_balances AS inserted
    JOIN pos_visual_seed_products AS seed
      ON md5('carnicerias-pos-visual-product:' || seed.code)::uuid = inserted."ProductId";
END
$seed$;

COMMIT;
