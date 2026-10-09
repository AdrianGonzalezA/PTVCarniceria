-- Development-only fiscal fixture for the single Carnicerias application database.
-- Re-running this script leaves already-active 21% rules untouched.
BEGIN;

DO $tax$
DECLARE
    company_id uuid;
    actor_id uuid;
    product_id uuid;
    current_id uuid;
    current_treatment integer;
    current_rate numeric;
    current_from timestamptz;
    effective_at timestamptz;
BEGIN
    IF current_database() <> 'carnicerias_test_visual' THEN
        RAISE EXCEPTION 'This fixture only applies to carnicerias_test_visual';
    END IF;

    SELECT "Id" INTO STRICT company_id
    FROM platform_access.companies
    WHERE "Name" = 'Empresa Visual' AND "IsActive";

    SELECT "Id" INTO STRICT actor_id
    FROM platform_access.users
    WHERE "Username" = 'visual-admin' AND "IsActive";

    LOCK TABLE catalog_pricing.product_tax_rules IN SHARE ROW EXCLUSIVE MODE;

    FOR product_id IN
        SELECT "Id" FROM catalog_pricing.products
        WHERE "CompanyId" = company_id
        ORDER BY "Id"
    LOOP
        SELECT "Id", "Treatment", "RatePercent", "EffectiveFromUtc"
        INTO current_id, current_treatment, current_rate, current_from
        FROM catalog_pricing.product_tax_rules
        WHERE "CompanyId" = company_id AND "ProductId" = product_id
          AND "EffectiveToUtc" IS NULL;

        IF current_id IS NOT NULL AND current_treatment = 0 AND current_rate = 21 THEN
            CONTINUE;
        END IF;

        effective_at := clock_timestamp();
        IF current_id IS NOT NULL THEN
            effective_at := GREATEST(effective_at, current_from + interval '1 microsecond');
            UPDATE catalog_pricing.product_tax_rules
            SET "EffectiveToUtc" = effective_at
            WHERE "Id" = current_id;
        END IF;

        INSERT INTO catalog_pricing.product_tax_rules
            ("Id", "CompanyId", "ProductId", "Treatment", "RatePercent",
             "ChangedByUserId", "EffectiveFromUtc", "EffectiveToUtc")
        VALUES (gen_random_uuid(), company_id, product_id, 0, 21.00,
                actor_id, effective_at, NULL);
    END LOOP;

    IF EXISTS (
        SELECT 1 FROM catalog_pricing.products AS product
        WHERE product."CompanyId" = company_id AND NOT EXISTS (
            SELECT 1 FROM catalog_pricing.product_tax_rules AS rule
            WHERE rule."CompanyId" = company_id AND rule."ProductId" = product."Id"
              AND rule."EffectiveToUtc" IS NULL
              AND rule."Treatment" = 0 AND rule."RatePercent" = 21.00
        )
    ) THEN
        RAISE EXCEPTION 'Not every visual product has an active 21%% VAT rule';
    END IF;
END
$tax$;

COMMIT;
