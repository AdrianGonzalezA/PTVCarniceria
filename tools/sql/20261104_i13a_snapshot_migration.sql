START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104184401_AddFiscalDocumentSnapshots') THEN
    CREATE TABLE pos_sales.fiscal_document_snapshots (
        "FiscalDocumentId" uuid NOT NULL,
        "ReceiverVatConditionCode" integer NOT NULL,
        "Total" numeric(12,2) NOT NULL,
        "TaxableBase" numeric(12,2) NOT NULL,
        "VatAmount" numeric(12,2) NOT NULL,
        "ExemptAmount" numeric(12,2) NOT NULL,
        "NotTaxedAmount" numeric(12,2) NOT NULL,
        CONSTRAINT "PK_fiscal_document_snapshots" PRIMARY KEY ("FiscalDocumentId"),
        CONSTRAINT "CK_fiscal_document_snapshots_amounts" CHECK ("Total" > 0 AND "TaxableBase" >= 0 AND "VatAmount" >= 0 AND "ExemptAmount" >= 0 AND "NotTaxedAmount" >= 0 AND "ReceiverVatConditionCode" > 0 AND "Total" = "TaxableBase" + "VatAmount" + "ExemptAmount" + "NotTaxedAmount"),
        CONSTRAINT "FK_fiscal_document_snapshots_fiscal_documents_FiscalDocumentId" FOREIGN KEY ("FiscalDocumentId") REFERENCES pos_sales.fiscal_documents ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104184401_AddFiscalDocumentSnapshots') THEN
    CREATE TABLE pos_sales.fiscal_document_vat_lines (
        "FiscalDocumentId" uuid NOT NULL,
        "ArcaRateCode" integer NOT NULL,
        "RatePercent" numeric(5,2) NOT NULL,
        "TaxableBase" numeric(12,2) NOT NULL,
        "TaxAmount" numeric(12,2) NOT NULL,
        CONSTRAINT "PK_fiscal_document_vat_lines" PRIMARY KEY ("FiscalDocumentId", "ArcaRateCode"),
        CONSTRAINT "CK_fiscal_document_vat_lines_amounts" CHECK ("ArcaRateCode" > 0 AND "RatePercent" >= 0 AND "TaxableBase" > 0 AND "TaxAmount" >= 0),
        CONSTRAINT "FK_fiscal_document_vat_lines_fiscal_document_snapshots_FiscalD~" FOREIGN KEY ("FiscalDocumentId") REFERENCES pos_sales.fiscal_document_snapshots ("FiscalDocumentId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104184401_AddFiscalDocumentSnapshots') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261104184401_AddFiscalDocumentSnapshots', '10.0.12');
    END IF;
END $EF$;
COMMIT;
