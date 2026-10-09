using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.Infrastructure;

public sealed class PlatformAccessDbContext(DbContextOptions<PlatformAccessDbContext> options)
    : DbContext(options)
{
    public DbSet<UserIdentity> Users => Set<UserIdentity>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<PermissionDefinition> Permissions => Set<PermissionDefinition>();

    public DbSet<RoleCapability> RolePermissions => Set<RoleCapability>();

    public DbSet<UserAssignment> UserAssignments => Set<UserAssignment>();

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<PosTerminal> PosTerminals => Set<PosTerminal>();

    public DbSet<ArcaCompanySettings> ArcaCompanySettings => Set<ArcaCompanySettings>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<PriceList> PriceLists => Set<PriceList>();

    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();

    public DbSet<CustomerSaleCharge> CustomerSaleCharges => Set<CustomerSaleCharge>();

    public DbSet<CustomerCollectionReceipt> CustomerCollectionReceipts => Set<CustomerCollectionReceipt>();

    public DbSet<CustomerCollectionAllocation> CustomerCollectionAllocations => Set<CustomerCollectionAllocation>();

    public DbSet<CustomerCollectionCorrection> CustomerCollectionCorrections => Set<CustomerCollectionCorrection>();

    public DbSet<CustomerCreditApplication> CustomerCreditApplications => Set<CustomerCreditApplication>();

    public DbSet<BranchPriceList> BranchPriceLists => Set<BranchPriceList>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();

    public DbSet<ProductCode> ProductCodes => Set<ProductCode>();

    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();

    public DbSet<ProductCostVersion> ProductCostVersions => Set<ProductCostVersion>();

    public DbSet<AdminImportOperation> AdminImportOperations => Set<AdminImportOperation>();

    public DbSet<ProductTaxRule> ProductTaxRules => Set<ProductTaxRule>();

    public DbSet<TaxCatalogEntry> TaxCatalogEntries => Set<TaxCatalogEntry>();

    public DbSet<OtherTaxAssignment> OtherTaxAssignments => Set<OtherTaxAssignment>();

    public DbSet<SaleDraft> SaleDrafts => Set<SaleDraft>();

    public DbSet<BranchInventoryBalance> BranchInventoryBalances => Set<BranchInventoryBalance>();

    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    public DbSet<BarcodeProfile> BarcodeProfiles => Set<BarcodeProfile>();

    public DbSet<InventoryPiece> InventoryPieces => Set<InventoryPiece>();

    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();

    public DbSet<ConfirmedSale> ConfirmedSales => Set<ConfirmedSale>();

    public DbSet<FiscalDocument> FiscalDocuments => Set<FiscalDocument>();

    public DbSet<ConfirmedSaleLine> ConfirmedSaleLines => Set<ConfirmedSaleLine>();

    public DbSet<SalePayment> SalePayments => Set<SalePayment>();

    public DbSet<PointPaymentIntent> PointPaymentIntents => Set<PointPaymentIntent>();

    public DbSet<CashLedgerMovement> CashLedgerMovements => Set<CashLedgerMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("platform_access");

        modelBuilder.Entity<UserIdentity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Username).HasMaxLength(100).IsRequired();
            entity.Property(user => user.UsernameNormalized).HasMaxLength(100).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.EmailNormalized).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.HasIndex(user => user.UsernameNormalized).IsUnique();
            entity.HasIndex(user => user.EmailNormalized).IsUnique();
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Code).HasMaxLength(100).IsRequired();
            entity.Property(role => role.Name).HasMaxLength(160).IsRequired();
            entity.HasIndex(role => role.Code).IsUnique();
        });

        modelBuilder.Entity<PermissionDefinition>(entity =>
        {
            entity.ToTable("permissions");
            entity.HasKey(permission => permission.Id);
            entity.Property(permission => permission.Code).HasMaxLength(160).IsRequired();
            entity.Property(permission => permission.Description).HasMaxLength(300).IsRequired();
            entity.HasIndex(permission => permission.Code).IsUnique();
        });

        modelBuilder.Entity<RoleCapability>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(link => new { link.RoleId, link.PermissionId });
            entity.HasOne<Role>().WithMany().HasForeignKey(link => link.RoleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PermissionDefinition>().WithMany().HasForeignKey(link => link.PermissionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(company => company.Name).IsUnique();
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("branches");
            entity.HasKey(branch => branch.Id);
            entity.Property(branch => branch.Name).HasMaxLength(200).IsRequired();
            entity.HasAlternateKey(branch => new { branch.CompanyId, branch.Id });
            entity.HasIndex(branch => new { branch.CompanyId, branch.Name }).IsUnique();
            entity.HasOne<Company>().WithMany().HasForeignKey(branch => branch.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosTerminal>(entity =>
        {
            entity.ToTable("pos_terminals");
            entity.HasKey(terminal => terminal.Id);
            entity.HasAlternateKey(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Id });
            entity.Property(terminal => terminal.Name).HasMaxLength(120).IsRequired();
            entity.Property(terminal => terminal.CredentialHash).HasMaxLength(64);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(terminal => new { terminal.CompanyId, terminal.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Name })
                .IsUnique();
            entity.HasIndex(terminal => terminal.CredentialHash)
                .IsUnique().HasFilter("\"CredentialHash\" IS NOT NULL");
        });

        modelBuilder.Entity<ArcaCompanySettings>(entity =>
        {
            entity.ToTable("arca_company_settings", table =>
            {
                table.HasCheckConstraint("CK_arca_company_settings_point_of_sale",
                    "\"PointOfSale\" > 0 AND \"PointOfSale\" < 99999");
                table.HasCheckConstraint("CK_arca_company_settings_certificate_range",
                    "\"CertificateNotAfterUtc\" IS NULL OR \"CertificateNotAfterUtc\" > \"CertificateNotBeforeUtc\"");
            });
            entity.HasKey(settings => settings.CompanyId);
            entity.Property(settings => settings.IssuerCuit).HasMaxLength(11).IsRequired();
            entity.Property(settings => settings.IssuerName).HasMaxLength(200).IsRequired();
            entity.Property(settings => settings.IssuerAddress).HasMaxLength(300).IsRequired();
            entity.Property(settings => settings.IssuerIibb).HasMaxLength(40);
            entity.Property(settings => settings.CertificateSubject).HasMaxLength(500);
            entity.Property(settings => settings.CertificateThumbprint).HasMaxLength(40);
            entity.HasOne<Company>().WithOne().HasForeignKey<ArcaCompanySettings>(settings => settings.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(settings => settings.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserAssignment>(entity =>
        {
            entity.ToTable("user_assignments");
            entity.HasKey(assignment => assignment.Id);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(assignment => assignment.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Role>().WithMany().HasForeignKey(assignment => assignment.RoleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>().WithMany().HasForeignKey(assignment => assignment.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(assignment => new { assignment.CompanyId, assignment.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(assignment => new { assignment.UserId, assignment.RoleId, assignment.CompanyId })
                .IsUnique()
                .HasFilter("\"BranchId\" IS NULL");
            entity.HasIndex(assignment => new { assignment.UserId, assignment.RoleId, assignment.CompanyId, assignment.BranchId })
                .IsUnique()
                .HasFilter("\"BranchId\" IS NOT NULL");
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("sessions", table => table.HasCheckConstraint(
                "CK_sessions_context_pair",
                "(\"CompanyId\" IS NULL AND \"BranchId\" IS NULL) OR (\"CompanyId\" IS NOT NULL AND \"BranchId\" IS NOT NULL)"));
            entity.HasKey(session => session.Id);
            entity.Property(session => session.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(session => session.TokenHash).IsUnique();
            entity.HasIndex(session => new { session.UserId, session.ExpiresAtUtc });
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>().WithMany().HasForeignKey(session => session.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(session => new { session.CompanyId, session.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany()
                .HasForeignKey(session => session.PosTerminalId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PriceList>(entity =>
        {
            entity.ToTable("price_lists", "catalog_pricing");
            entity.HasKey(list => list.Id);
            entity.HasAlternateKey(list => new { list.CompanyId, list.Id });
            entity.Property(list => list.Name).HasMaxLength(160).IsRequired();
            entity.HasIndex(list => new { list.CompanyId, list.Name }).IsUnique();
            entity.HasOne<Company>().WithMany().HasForeignKey(list => list.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerAccount>(entity =>
        {
            entity.ToTable("customers", "customers_credit");
            entity.HasKey(customer => customer.Id);
            entity.HasAlternateKey(customer => new { customer.CompanyId, customer.Id });
            entity.Property(customer => customer.Code).HasMaxLength(80).IsRequired();
            entity.Property(customer => customer.NormalizedCode).HasMaxLength(80).IsRequired();
            entity.Property(customer => customer.Name).HasMaxLength(200).IsRequired();
            entity.Ignore(customer => customer.CanChargeToAccount);
            entity.HasOne<Company>().WithMany().HasForeignKey(customer => customer.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(customer => new { customer.CompanyId, customer.NormalizedCode }).IsUnique();
            entity.HasIndex(customer => new { customer.CompanyId, customer.Name });
        });

        modelBuilder.Entity<CustomerSaleCharge>(entity =>
        {
            entity.ToTable("sale_charges", "customers_credit", table =>
                table.HasCheckConstraint("CK_sale_charges_amount_positive", "\"Amount\" > 0"));
            entity.HasKey(charge => charge.Id);
            entity.HasAlternateKey(charge => new { charge.CompanyId, charge.CustomerId, charge.SaleId });
            entity.Property(charge => charge.Amount).HasPrecision(12, 2);
            entity.HasOne<CustomerAccount>().WithMany()
                .HasForeignKey(charge => new { charge.CompanyId, charge.CustomerId })
                .HasPrincipalKey(customer => new { customer.CompanyId, customer.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ConfirmedSale>().WithMany()
                .HasForeignKey(charge => new { charge.CompanyId, charge.SaleId })
                .HasPrincipalKey(sale => new { sale.CompanyId, sale.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(charge => new { charge.CompanyId, charge.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashierShift>().WithMany()
                .HasForeignKey(charge => new { charge.CompanyId, charge.CashierShiftId })
                .HasPrincipalKey(shift => new { shift.CompanyId, shift.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(charge => charge.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(charge => charge.SaleId).IsUnique();
            entity.HasIndex(charge => new { charge.CompanyId, charge.CustomerId, charge.CreatedAtUtc });
        });

        modelBuilder.Entity<CustomerCollectionReceipt>(entity =>
        {
            entity.ToTable("collection_receipts", "customers_credit", table =>
            {
                table.HasCheckConstraint("CK_collection_receipts_amounts",
                    "\"Amount\" > 0 AND \"CreditAmount\" >= 0 AND \"CreditAmount\" <= \"Amount\"");
                table.HasCheckConstraint("CK_collection_receipts_origin",
                    "\"Origin\" IN (0, 1) AND ((\"Origin\" = 0 AND \"ReplacesReceiptId\" IS NULL) OR (\"Origin\" = 1 AND \"ReplacesReceiptId\" IS NOT NULL))");
                table.HasCheckConstraint("CK_collection_receipts_void",
                    "\"IsVoided\" = (\"VoidedAtUtc\" IS NOT NULL)");
            });
            entity.HasKey(receipt => receipt.Id);
            entity.HasAlternateKey(receipt => new { receipt.CompanyId, receipt.Id });
            entity.Property(receipt => receipt.ReceiptNumber).ValueGeneratedOnAdd();
            entity.Property(receipt => receipt.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(receipt => receipt.Method).HasConversion<int>().IsRequired();
            entity.Property(receipt => receipt.Origin).HasConversion<int>().IsRequired();
            entity.Property(receipt => receipt.Amount).HasPrecision(12, 2);
            entity.Property(receipt => receipt.CreditAmount).HasPrecision(12, 2);
            entity.HasOne<CustomerAccount>().WithMany()
                .HasForeignKey(receipt => new { receipt.CompanyId, receipt.CustomerId })
                .HasPrincipalKey(customer => new { customer.CompanyId, customer.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(receipt => new { receipt.CompanyId, receipt.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashierShift>().WithMany()
                .HasForeignKey(receipt => new { receipt.CompanyId, receipt.CashierShiftId })
                .HasPrincipalKey(shift => new { shift.CompanyId, shift.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany()
                .HasForeignKey(receipt => new { receipt.CompanyId, receipt.BranchId, receipt.PosTerminalId })
                .HasPrincipalKey(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(receipt => receipt.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CustomerCollectionReceipt>().WithMany()
                .HasForeignKey(receipt => new { receipt.CompanyId, receipt.ReplacesReceiptId })
                .HasPrincipalKey(original => new { original.CompanyId, original.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(receipt => receipt.ReceiptNumber).IsUnique();
            entity.HasIndex(receipt => new { receipt.CompanyId, receipt.OperationId }).IsUnique();
            entity.HasIndex(receipt => new { receipt.CompanyId, receipt.CustomerId, receipt.CreatedAtUtc });
            entity.HasIndex(receipt => new { receipt.CompanyId, receipt.CashierShiftId });
            entity.HasMany(receipt => receipt.Allocations).WithOne(allocation => allocation.Receipt)
                .HasForeignKey(allocation => new { allocation.CompanyId, allocation.ReceiptId })
                .HasPrincipalKey(receipt => new { receipt.CompanyId, receipt.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerCollectionCorrection>(entity =>
        {
            entity.ToTable("collection_corrections", "customers_credit", table =>
            {
                table.HasCheckConstraint("CK_collection_corrections_amount", "\"Amount\" > 0");
                table.HasCheckConstraint("CK_collection_corrections_kind",
                    "\"Kind\" IN (0, 1) AND ((\"Kind\" = 0 AND \"ReplacementReceiptId\" IS NOT NULL) OR (\"Kind\" = 1 AND \"ReplacementReceiptId\" IS NULL))");
            });
            entity.HasKey(correction => correction.Id);
            entity.Property(correction => correction.CorrectionNumber).ValueGeneratedOnAdd();
            entity.Property(correction => correction.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(correction => correction.Reason).HasMaxLength(240).IsRequired();
            entity.Property(correction => correction.Kind).HasConversion<int>().IsRequired();
            entity.Property(correction => correction.Amount).HasPrecision(12, 2);
            entity.HasOne<CustomerCollectionReceipt>().WithMany()
                .HasForeignKey(correction => new { correction.CompanyId, correction.OriginalReceiptId })
                .HasPrincipalKey(receipt => new { receipt.CompanyId, receipt.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CustomerCollectionReceipt>().WithMany()
                .HasForeignKey(correction => new { correction.CompanyId, correction.ReplacementReceiptId })
                .HasPrincipalKey(receipt => new { receipt.CompanyId, receipt.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashierShift>().WithMany()
                .HasForeignKey(correction => new { correction.CompanyId, correction.CashierShiftId })
                .HasPrincipalKey(shift => new { shift.CompanyId, shift.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany()
                .HasForeignKey(correction => new { correction.CompanyId, correction.BranchId, correction.PosTerminalId })
                .HasPrincipalKey(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(correction => correction.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(correction => correction.CorrectionNumber).IsUnique();
            entity.HasIndex(correction => new { correction.CompanyId, correction.OperationId }).IsUnique();
            entity.HasIndex(correction => new { correction.CompanyId, correction.OriginalReceiptId }).IsUnique();
            entity.HasIndex(correction => new { correction.CompanyId, correction.CreatedAtUtc });
        });

        modelBuilder.Entity<CustomerCollectionAllocation>(entity =>
        {
            entity.ToTable("collection_allocations", "customers_credit", table =>
                table.HasCheckConstraint("CK_collection_allocations_amount_positive", "\"Amount\" > 0"));
            entity.HasKey(allocation => allocation.Id);
            entity.Property(allocation => allocation.Amount).HasPrecision(12, 2);
            entity.HasOne<CustomerSaleCharge>().WithMany()
                .HasForeignKey(allocation => new { allocation.CompanyId, allocation.CustomerId, allocation.SaleId })
                .HasPrincipalKey(charge => new { charge.CompanyId, charge.CustomerId, charge.SaleId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(allocation => new { allocation.ReceiptId, allocation.SaleId }).IsUnique();
            entity.HasIndex(allocation => new { allocation.CompanyId, allocation.CustomerId, allocation.SaleId });
        });

        modelBuilder.Entity<CustomerCreditApplication>(entity =>
        {
            entity.ToTable("credit_applications", "customers_credit", table =>
                table.HasCheckConstraint("CK_credit_applications_amount_positive", "\"Amount\" > 0"));
            entity.HasKey(application => application.Id);
            entity.Property(application => application.Amount).HasPrecision(12, 2);
            entity.HasOne<CustomerAccount>().WithMany()
                .HasForeignKey(application => new { application.CompanyId, application.CustomerId })
                .HasPrincipalKey(customer => new { customer.CompanyId, customer.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ConfirmedSale>().WithMany()
                .HasForeignKey(application => new { application.CompanyId, application.SaleId })
                .HasPrincipalKey(sale => new { sale.CompanyId, sale.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashierShift>().WithMany()
                .HasForeignKey(application => new { application.CompanyId, application.CashierShiftId })
                .HasPrincipalKey(shift => new { shift.CompanyId, shift.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany()
                .HasForeignKey(application => application.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(application => application.SaleId).IsUnique();
            entity.HasIndex(application => new { application.CompanyId, application.CustomerId, application.CreatedAtUtc });
        });

        modelBuilder.Entity<BranchPriceList>(entity =>
        {
            entity.ToTable("branch_price_lists", "catalog_pricing");
            entity.HasKey(link => new { link.BranchId, link.PriceListId });
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(link => new { link.CompanyId, link.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PriceList>().WithMany()
                .HasForeignKey(link => new { link.CompanyId, link.PriceListId })
                .HasPrincipalKey(list => new { list.CompanyId, list.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(link => new { link.CompanyId, link.BranchId, link.IsActive });
        });

        modelBuilder.Entity<ProductCategory>(entity =>
        {
            entity.ToTable("categories", "catalog_pricing");
            entity.HasKey(category => category.Id);
            entity.HasAlternateKey(category => new { category.CompanyId, category.Id });
            entity.Property(category => category.Name).HasMaxLength(120).IsRequired();
            entity.HasOne<Company>().WithMany().HasForeignKey(category => category.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(category => new { category.CompanyId, category.Name }).IsUnique();
        });

        modelBuilder.Entity<BarcodeProfile>(entity =>
        {
            entity.ToTable("barcode_profiles", "inventory");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.Name).HasMaxLength(120).IsRequired();
            entity.Property(profile => profile.NormalizedName).HasMaxLength(120).IsRequired();
            entity.Property(profile => profile.Formula).HasMaxLength(512).IsRequired();
            entity.Property(profile => profile.WeightField).HasMaxLength(80).IsRequired();
            entity.HasOne<Company>().WithMany().HasForeignKey(profile => profile.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(profile => new { profile.CompanyId, profile.NormalizedName, profile.Revision })
                .IsUnique();
        });

        modelBuilder.Entity<InventoryPiece>(entity =>
        {
            entity.ToTable("pieces", "inventory", table =>
                table.HasCheckConstraint("CK_pieces_received_weight_positive", "\"ReceivedWeightKg\" > 0"));
            entity.HasKey(piece => piece.Id);
            entity.Property(piece => piece.SourceSystem).HasMaxLength(120).IsRequired();
            entity.Property(piece => piece.NormalizedSourceSystem).HasMaxLength(120).IsRequired();
            entity.Property(piece => piece.ExternalIdentifier).HasMaxLength(80).IsRequired();
            entity.Property(piece => piece.RawBarcode).HasMaxLength(80).IsRequired();
            entity.Property(piece => piece.IdentifierField).HasMaxLength(80).IsRequired();
            entity.Property(piece => piece.ReceivedWeightKg).HasPrecision(12, 3);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(piece => new { piece.CompanyId, piece.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(piece => new { piece.CompanyId, piece.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BarcodeProfile>().WithMany().HasForeignKey(piece => piece.BarcodeProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InventoryMovement>().WithMany().HasForeignKey(piece => piece.InventoryMovementId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(piece => piece.ReceivedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(piece => new
            {
                piece.CompanyId,
                piece.NormalizedSourceSystem,
                piece.ExternalIdentifier
            }).IsUnique();
            entity.HasIndex(piece => new { piece.CompanyId, piece.BranchId, piece.OperationId }).IsUnique();
            entity.HasIndex(piece => new
            {
                piece.CompanyId,
                piece.BranchId,
                piece.ProductId,
                piece.ReceivedAtUtc
            });
        });

        modelBuilder.Entity<CatalogProduct>(entity =>
        {
            entity.ToTable("products", "catalog_pricing", table =>
            {
                table.HasCheckConstraint("CK_products_cost_positive", "\"Cost\" > 0");
                table.HasCheckConstraint("CK_products_sale_mode", "\"SaleMode\" IN (0, 1)");
            });
            entity.HasKey(product => product.Id);
            entity.HasAlternateKey(product => new { product.CompanyId, product.Id });
            entity.Property(product => product.Code).HasMaxLength(80).IsRequired();
            entity.Property(product => product.NormalizedCode).HasMaxLength(80).IsRequired();
            entity.Property(product => product.Name).HasMaxLength(200).IsRequired();
            entity.Property(product => product.Unit).HasMaxLength(24).IsRequired();
            entity.Property(product => product.Cost).HasPrecision(12, 2);
            entity.HasOne<Company>().WithMany().HasForeignKey(product => product.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductCategory>().WithMany()
                .HasForeignKey(product => new { product.CompanyId, product.CategoryId })
                .HasPrincipalKey(category => new { category.CompanyId, category.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(product => new { product.CompanyId, product.NormalizedCode }).IsUnique();
            entity.HasIndex(product => new { product.CompanyId, product.CategoryId, product.IsActive });
        });

        modelBuilder.Entity<TaxCatalogEntry>(entity =>
        {
            entity.ToTable("tax_catalog_entries", "catalog_pricing", table =>
            {
                table.HasCheckConstraint("CK_tax_catalog_entries_kind", "\"Kind\" IN (0, 1)");
                table.HasCheckConstraint("CK_tax_catalog_entries_rate",
                    "\"RatePercent\" >= 0 AND \"RatePercent\" <= 100");
            });
            entity.HasKey(tax => tax.Id);
            entity.HasAlternateKey(tax => new { tax.CompanyId, tax.Id });
            entity.Property(tax => tax.Code).HasMaxLength(40).IsRequired();
            entity.Property(tax => tax.Name).HasMaxLength(120).IsRequired();
            entity.Property(tax => tax.Kind).HasConversion<int>();
            entity.Property(tax => tax.RatePercent).HasPrecision(5, 2);
            entity.HasOne<Company>().WithMany().HasForeignKey(tax => tax.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(tax => tax.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(tax => tax.DeactivatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(tax => new { tax.CompanyId, tax.Code }).IsUnique();
        });

        modelBuilder.Entity<ProductTaxRule>(entity =>
        {
            entity.ToTable("product_tax_rules", "catalog_pricing", table =>
            {
                table.HasCheckConstraint("CK_product_tax_rules_treatment",
                    "\"Treatment\" IN (0, 1, 2) AND (\"Treatment\" = 0 OR \"RatePercent\" = 0)");
                table.HasCheckConstraint("CK_product_tax_rules_catalog_treatment",
                    "\"TaxCatalogEntryId\" IS NULL OR \"Treatment\" = 0");
                table.HasCheckConstraint("CK_product_tax_rules_rate",
                    "\"RatePercent\" >= 0 AND \"RatePercent\" <= 100");
                table.HasCheckConstraint("CK_product_tax_rules_dates",
                    "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
            });
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.RatePercent).HasPrecision(5, 2);
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(rule => new { rule.CompanyId, rule.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaxCatalogEntry>().WithMany()
                .HasForeignKey(rule => new { rule.CompanyId, rule.TaxCatalogEntryId })
                .HasPrincipalKey(tax => new { tax.CompanyId, tax.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(rule => rule.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(rule => new { rule.CompanyId, rule.ProductId, rule.EffectiveToUtc })
                .HasFilter("\"EffectiveToUtc\" IS NULL").IsUnique();
            entity.HasIndex(rule => new { rule.CompanyId, rule.ProductId, rule.EffectiveFromUtc })
                .IsUnique();
        });

        modelBuilder.Entity<OtherTaxAssignment>(entity =>
        {
            entity.ToTable("other_tax_assignments", "catalog_pricing", table =>
                table.HasCheckConstraint("CK_other_tax_assignments_dates",
                    "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\""));
            entity.HasKey(assignment => assignment.Id);
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(assignment => new { assignment.CompanyId, assignment.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaxCatalogEntry>().WithMany()
                .HasForeignKey(assignment => new { assignment.CompanyId, assignment.TaxCatalogEntryId })
                .HasPrincipalKey(tax => new { tax.CompanyId, tax.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany()
                .HasForeignKey(assignment => assignment.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany()
                .HasForeignKey(assignment => assignment.RemovedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(assignment => new
                { assignment.CompanyId, assignment.ProductId, assignment.TaxCatalogEntryId,
                    assignment.EffectiveToUtc })
                .HasFilter("\"EffectiveToUtc\" IS NULL").IsUnique();
            entity.HasIndex(assignment => new
                { assignment.CompanyId, assignment.TaxCatalogEntryId, assignment.EffectiveToUtc });
        });

        modelBuilder.Entity<AdminImportOperation>(entity =>
        {
            entity.ToTable("admin_import_operations", "catalog_pricing");
            entity.HasKey(operation => new { operation.CompanyId, operation.OperationId });
            entity.Property(operation => operation.Kind).HasMaxLength(32).IsRequired();
            entity.Property(operation => operation.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(operation => operation.ResultJson).HasColumnType("jsonb").IsRequired();
            entity.HasOne<Company>().WithMany().HasForeignKey(operation => operation.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(operation => operation.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductCostVersion>(entity =>
        {
            entity.ToTable("product_cost_versions", "catalog_pricing", table =>
            {
                table.HasCheckConstraint("CK_product_cost_versions_amount_positive", "\"Amount\" > 0");
                table.HasCheckConstraint("CK_product_cost_versions_effective_range",
                    "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
            });
            entity.HasKey(cost => cost.Id);
            entity.Property(cost => cost.Amount).HasPrecision(12, 2);
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(cost => new { cost.CompanyId, cost.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(cost => cost.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(cost => new { cost.CompanyId, cost.ProductId, cost.EffectiveToUtc })
                .HasFilter("\"EffectiveToUtc\" IS NULL").IsUnique();
            entity.HasIndex(cost => new { cost.CompanyId, cost.ProductId, cost.EffectiveFromUtc })
                .IsUnique();
        });

        modelBuilder.Entity<ProductCode>(entity =>
        {
            entity.ToTable("product_codes", "catalog_pricing");
            entity.HasKey(code => new { code.ProductId, code.NormalizedCode });
            entity.Property(code => code.Code).HasMaxLength(80).IsRequired();
            entity.Property(code => code.NormalizedCode).HasMaxLength(80).IsRequired();
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(code => new { code.CompanyId, code.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(code => new { code.CompanyId, code.NormalizedCode }).IsUnique();
        });

        modelBuilder.Entity<ProductPrice>(entity =>
        {
            entity.ToTable("product_prices", "catalog_pricing", table =>
            {
                table.HasCheckConstraint("CK_product_prices_amount_positive", "\"Amount\" > 0");
                table.HasCheckConstraint(
                    "CK_product_prices_effective_range",
                    "\"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" > \"EffectiveFromUtc\"");
            });
            entity.HasKey(price => price.Id);
            entity.Property(price => price.Amount).HasPrecision(12, 2);
            entity.HasOne<PriceList>().WithMany()
                .HasForeignKey(price => new { price.CompanyId, price.PriceListId })
                .HasPrincipalKey(list => new { list.CompanyId, list.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(price => new { price.CompanyId, price.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(price => price.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(price => new
            {
                price.CompanyId,
                price.PriceListId,
                price.ProductId,
                price.EffectiveFromUtc
            }).IsUnique();
            entity.HasIndex(price => new { price.CompanyId, price.PriceListId, price.ProductId })
                .IsUnique()
                .HasFilter("\"EffectiveToUtc\" IS NULL");
        });

        modelBuilder.Entity<SaleDraft>(entity =>
        {
            entity.ToTable("sale_drafts", "pos_sales", table => table.HasCheckConstraint(
                "CK_sale_drafts_discount", "\"DiscountAmount\" >= 0 AND (\"DiscountAmount\" = 0 OR \"DiscountReason\" IS NOT NULL)"));
            entity.HasKey(draft => draft.Id);
            entity.HasAlternateKey(draft => new { draft.CompanyId, draft.Id });
            entity.Property(draft => draft.CreatedAtUtc).IsRequired();
            entity.Property(draft => draft.UpdatedAtUtc).IsRequired();
            entity.Property(draft => draft.Status).HasConversion<int>().IsRequired();
            entity.Property(draft => draft.TicketSlot).HasConversion<string>().HasMaxLength(1).IsRequired();
            entity.Property(draft => draft.DiscountAmount).HasPrecision(12, 2);
            entity.Property(draft => draft.DiscountReason).HasMaxLength(200);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(draft => new { draft.CompanyId, draft.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PriceList>().WithMany()
                .HasForeignKey(draft => new { draft.CompanyId, draft.PriceListId })
                .HasPrincipalKey(list => new { list.CompanyId, list.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(draft => draft.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany()
                .HasForeignKey(draft => new { draft.CompanyId, draft.BranchId, draft.PosTerminalId })
                .HasPrincipalKey(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashierShift>().WithMany()
                .HasForeignKey(draft => new { draft.CompanyId, draft.CashierShiftId })
                .HasPrincipalKey(shift => new { shift.CompanyId, shift.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(draft => draft.CancelledByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(draft => draft.Lines).WithOne()
                .HasForeignKey(line => line.SaleDraftId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(draft => new { draft.CompanyId, draft.BranchId, draft.UserId, draft.TicketSlot })
                .IsUnique().HasFilter("\"Status\" = 0");
        });

        modelBuilder.Entity<SaleDraftLine>(entity =>
        {
            entity.ToTable("sale_draft_lines", "pos_sales", table =>
            {
                table.HasCheckConstraint("CK_sale_draft_lines_quantity_positive", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_sale_draft_lines_price_positive", "\"UnitPrice\" > 0");
            });
            entity.HasKey(line => line.Id);
            entity.Property(line => line.Id).ValueGeneratedNever();
            entity.Property(line => line.ProductCode).HasMaxLength(80).IsRequired();
            entity.Property(line => line.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(line => line.Unit).HasMaxLength(24).IsRequired();
            entity.Property(line => line.SaleMode).HasConversion<int>().IsRequired();
            entity.Property(line => line.Quantity).HasPrecision(12, 3);
            entity.Property(line => line.UnitPrice).HasPrecision(12, 2);
            entity.Property(line => line.PieceIdentifier).HasMaxLength(80);
            entity.HasOne<InventoryPiece>().WithMany().HasForeignKey(line => line.InventoryPieceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(line => line.SaleDraftId);
        });

        modelBuilder.Entity<ConfirmedSale>(entity =>
        {
            entity.ToTable("confirmed_sales", "pos_sales", table =>
            {
                table.HasCheckConstraint("CK_confirmed_sales_total_positive", "\"Total\" > 0");
                table.HasCheckConstraint("CK_confirmed_sales_discount", "\"DiscountAmount\" >= 0 AND (\"DiscountAmount\" = 0 OR \"DiscountReason\" IS NOT NULL)");
                table.HasCheckConstraint("CK_confirmed_sales_account_charge",
                    "\"AccountChargeAmount\" >= 0 AND \"CreditAppliedAmount\" >= 0 AND \"AccountChargeAmount\" + \"CreditAppliedAmount\" <= \"Total\" AND (\"AccountChargeAmount\" + \"CreditAppliedAmount\" = 0 OR (\"CustomerId\" IS NOT NULL AND \"CustomerCode\" IS NOT NULL AND \"CustomerName\" IS NOT NULL))");
                table.HasCheckConstraint("CK_confirmed_sales_document_type", "\"DocumentType\" IN (0, 1, 2) AND \"RecipientTaxStatus\" IN (0, 1, 2, 3)");
            });
            entity.HasKey(sale => sale.Id);
            entity.HasAlternateKey(sale => new { sale.CompanyId, sale.Id });
            entity.Property(sale => sale.Total).HasPrecision(12, 2);
            entity.Property(sale => sale.AccountChargeAmount).HasPrecision(12, 2);
            entity.Property(sale => sale.CreditAppliedAmount).HasPrecision(12, 2);
            entity.Property(sale => sale.DiscountAmount).HasPrecision(12, 2);
            entity.Property(sale => sale.DiscountReason).HasMaxLength(200);
            entity.Property(sale => sale.CustomerCode).HasMaxLength(80);
            entity.Property(sale => sale.CustomerName).HasMaxLength(200);
            entity.Property(sale => sale.RecipientName).HasMaxLength(200);
            entity.Property(sale => sale.RecipientDocumentNumber).HasMaxLength(11);
            entity.Property(sale => sale.RecipientAddress).HasMaxLength(200);
            entity.Property(sale => sale.PaymentRequestHash).HasMaxLength(64).IsRequired();
            entity.HasOne<CustomerAccount>().WithMany()
                .HasForeignKey(sale => new { sale.CompanyId, sale.CustomerId })
                .HasPrincipalKey(customer => new { customer.CompanyId, customer.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(sale => new { sale.CompanyId, sale.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(sale => sale.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany()
                .HasForeignKey(sale => new { sale.CompanyId, sale.BranchId, sale.PosTerminalId })
                .HasPrincipalKey(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashierShift>().WithMany()
                .HasForeignKey(sale => new { sale.CompanyId, sale.CashierShiftId })
                .HasPrincipalKey(shift => new { shift.CompanyId, shift.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PriceList>().WithMany()
                .HasForeignKey(sale => new { sale.CompanyId, sale.PriceListId })
                .HasPrincipalKey(list => new { list.CompanyId, list.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SaleDraft>().WithMany()
                .HasForeignKey(sale => new { sale.CompanyId, sale.SourceDraftId })
                .HasPrincipalKey(draft => new { draft.CompanyId, draft.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(sale => new { sale.CompanyId, sale.SourceDraftId }).IsUnique();
            entity.HasIndex(sale => new { sale.CompanyId, sale.ConfirmedAtUtc });
            entity.HasIndex(sale => new { sale.CompanyId, sale.BranchId, sale.PosTerminalId, sale.ConfirmedAtUtc });
            entity.HasMany(sale => sale.Lines).WithOne().HasForeignKey(line => line.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(sale => sale.Payments).WithOne().HasForeignKey(payment => payment.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FiscalDocument>(entity =>
        {
            entity.ToTable("fiscal_documents", "pos_sales", table =>
            {
                table.HasCheckConstraint("CK_fiscal_documents_status", "\"Status\" IN (0, 1, 2, 3)");
                table.HasCheckConstraint("CK_fiscal_documents_authorization",
                    "(\"Status\" = 3 AND \"Cae\" IS NOT NULL AND \"CaeExpiry\" IS NOT NULL AND \"AuthorizedAtUtc\" IS NOT NULL) OR (\"Status\" <> 3 AND \"Cae\" IS NULL AND \"CaeExpiry\" IS NULL AND \"AuthorizedAtUtc\" IS NULL)");
            });
            entity.HasKey(document => document.Id);
            entity.Property(document => document.IssuerCuit).HasMaxLength(11).IsRequired();
            entity.Property(document => document.IssuerName).HasMaxLength(200);
            entity.Property(document => document.IssuerAddress).HasMaxLength(300);
            entity.Property(document => document.IssuerIibb).HasMaxLength(40);
            entity.Property(document => document.Total).HasPrecision(12, 2);
            entity.Property(document => document.Cae).HasMaxLength(14);
            entity.Property(document => document.ErrorCodes).HasMaxLength(140);
            entity.Property(document => document.Status).HasConversion<int>();
            entity.HasOne<ConfirmedSale>().WithMany()
                .HasForeignKey(document => new { document.CompanyId, document.SaleId })
                .HasPrincipalKey(sale => new { sale.CompanyId, sale.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(document => new
            {
                document.IssuerCuit,
                document.PointOfSale,
                document.VoucherType,
                document.Number
            }).IsUnique();
            entity.HasIndex(document => new { document.CompanyId, document.SaleId })
                .IsUnique().HasFilter("\"Status\" <> 2");
        });

        modelBuilder.Entity<ConfirmedSaleLine>(entity =>
        {
            entity.ToTable("confirmed_sale_lines", "pos_sales", table =>
            {
                table.HasCheckConstraint("CK_confirmed_sale_lines_quantity_positive", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_confirmed_sale_lines_unit_price_positive", "\"UnitPrice\" > 0");
            });
            entity.HasKey(line => line.Id);
            entity.Property(line => line.ProductCode).HasMaxLength(80).IsRequired();
            entity.Property(line => line.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(line => line.Unit).HasMaxLength(24).IsRequired();
            entity.Property(line => line.SaleMode).HasConversion<int>().IsRequired();
            entity.Property(line => line.Quantity).HasPrecision(12, 3);
            entity.Property(line => line.UnitPrice).HasPrecision(12, 2);
            entity.Property(line => line.LineTotal).HasPrecision(12, 2);
            entity.Property(line => line.OrderDiscountAmount).HasPrecision(12, 2);
            entity.Property(line => line.NetAfterDiscount).HasPrecision(12, 2);
            entity.Property(line => line.TaxTreatment).HasConversion<int?>();
            entity.Property(line => line.TaxRatePercent).HasPrecision(5, 2);
            entity.Property(line => line.TaxableBase).HasPrecision(12, 2);
            entity.Property(line => line.TaxAmount).HasPrecision(12, 2);
            entity.Property(line => line.PieceIdentifier).HasMaxLength(80);
            entity.HasOne<InventoryPiece>().WithMany().HasForeignKey(line => line.InventoryPieceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(line => line.InventoryPieceId).IsUnique()
                .HasFilter("\"InventoryPieceId\" IS NOT NULL");
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalePayment>(entity =>
        {
            entity.ToTable("sale_payments", "payments_cash", table =>
            {
                table.HasCheckConstraint("CK_sale_payments_tendered_positive", "\"TenderedAmount\" > 0");
                table.HasCheckConstraint("CK_sale_payments_applied_positive", "\"AppliedAmount\" > 0");
            });
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.Method).HasConversion<int>().IsRequired();
            entity.Property(payment => payment.TenderedAmount).HasPrecision(12, 2);
            entity.Property(payment => payment.AppliedAmount).HasPrecision(12, 2);
        });

        modelBuilder.Entity<PointPaymentIntent>(entity =>
        {
            entity.ToTable("point_payment_intents", "payments_cash", table =>
            {
                table.HasCheckConstraint("CK_point_payment_intents_amount_positive", "\"Amount\" > 0");
                table.HasCheckConstraint("CK_point_payment_intents_status", "\"Status\" BETWEEN 0 AND 7");
            });
            entity.HasKey(intent => intent.Id);
            entity.Property(intent => intent.Amount).HasPrecision(12, 2);
            entity.Property(intent => intent.Status).HasConversion<int>().IsRequired();
            entity.Property(intent => intent.TerminalId).HasMaxLength(100).IsRequired();
            entity.Property(intent => intent.ProviderOrderId).HasMaxLength(100);
            entity.Property(intent => intent.ProviderPaymentId).HasMaxLength(100);
            entity.Property(intent => intent.ProviderOrderStatus).HasMaxLength(40);
            entity.Property(intent => intent.ProviderPaymentStatus).HasMaxLength(40);
            entity.Property(intent => intent.ProviderPaymentStatusDetail).HasMaxLength(80);
            entity.Ignore(intent => intent.ExternalReference);
            entity.HasOne<SaleDraft>().WithMany()
                .HasForeignKey(intent => new { intent.CompanyId, intent.SaleDraftId })
                .HasPrincipalKey(draft => new { draft.CompanyId, draft.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CashierShift>().WithMany()
                .HasForeignKey(intent => new { intent.CompanyId, intent.CashierShiftId })
                .HasPrincipalKey(shift => new { shift.CompanyId, shift.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany()
                .HasForeignKey(intent => new { intent.CompanyId, intent.BranchId, intent.PosTerminalId })
                .HasPrincipalKey(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany()
                .HasForeignKey(intent => intent.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(intent => intent.IdempotencyKey).IsUnique();
            entity.HasIndex(intent => intent.ProviderOrderId).IsUnique()
                .HasFilter("\"ProviderOrderId\" IS NOT NULL");
            entity.HasIndex(intent => new { intent.CompanyId, intent.SaleDraftId, intent.CreatedAtUtc });
        });

        modelBuilder.Entity<CashLedgerMovement>(entity =>
        {
            entity.ToTable("cash_ledger", "payments_cash", table =>
            {
                table.HasCheckConstraint("CK_cash_ledger_amount_nonzero", "\"AmountDelta\" <> 0");
                table.HasCheckConstraint("CK_cash_ledger_kind", "\"Kind\" IN (0, 1, 2, 3, 4)");
            });
            entity.HasKey(movement => movement.Id);
            entity.Property(movement => movement.Method).HasConversion<int>().IsRequired();
            entity.Property(movement => movement.Kind).HasConversion<int>().IsRequired();
            entity.Property(movement => movement.AmountDelta).HasPrecision(12, 2);
            entity.HasOne<CashierShift>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.CashierShiftId })
                .HasPrincipalKey(shift => new { shift.CompanyId, shift.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.BranchId, movement.PosTerminalId })
                .HasPrincipalKey(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(movement => movement.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ConfirmedSale>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.SaleId })
                .HasPrincipalKey(sale => new { sale.CompanyId, sale.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(movement => new { movement.CompanyId, movement.BranchId, movement.OperationId }).IsUnique();
            entity.HasIndex(movement => new { movement.CashierShiftId, movement.Method, movement.CreatedAtUtc });
            entity.HasIndex(movement => new { movement.CompanyId, movement.CreatedAtUtc });
            entity.HasIndex(movement => new { movement.CompanyId, movement.BranchId, movement.PosTerminalId, movement.CreatedAtUtc });
        });

        modelBuilder.Entity<BranchInventoryBalance>(entity =>
        {
            entity.ToTable("branch_inventory", "inventory", table =>
            {
                table.HasCheckConstraint("CK_branch_inventory_on_hand_nonnegative", "\"OnHand\" >= 0");
                table.HasCheckConstraint("CK_branch_inventory_reserved_range", "\"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\"");
            });
            entity.HasKey(balance => new { balance.CompanyId, balance.BranchId, balance.ProductId });
            entity.Property(balance => balance.OnHand).HasPrecision(12, 3);
            entity.Property(balance => balance.Reserved).HasPrecision(12, 3);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(balance => new { balance.CompanyId, balance.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(balance => new { balance.CompanyId, balance.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryMovement>(entity =>
        {
            entity.ToTable("inventory_movements", "inventory", table =>
            {
                table.HasCheckConstraint("CK_inventory_movements_delta_nonzero", "\"QuantityDelta\" <> 0");
                table.HasCheckConstraint("CK_inventory_movements_kind", "\"Kind\" IN (0, 1, 2, 3)");
            });
            entity.HasKey(movement => movement.Id);
            entity.Property(movement => movement.QuantityDelta).HasPrecision(12, 3);
            entity.Property(movement => movement.Reason).HasMaxLength(240).IsRequired();
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(movement => movement.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(movement => new { movement.CompanyId, movement.BranchId, movement.OperationId }).IsUnique();
            entity.HasIndex(movement => new { movement.CompanyId, movement.BranchId, movement.ProductId, movement.CreatedAtUtc });
            entity.HasIndex(movement => new { movement.CompanyId, movement.CreatedAtUtc });
            entity.HasIndex(movement => new { movement.CompanyId, movement.BranchId, movement.CreatedAtUtc });
        });

        modelBuilder.Entity<CashierShift>(entity =>
        {
            entity.ToTable("cashier_shifts", "payments_cash", table =>
            {
                table.HasCheckConstraint("CK_cashier_shifts_opening_cash_nonnegative", "\"OpeningCash\" >= 0");
                table.HasCheckConstraint("CK_cashier_shifts_status", "\"Status\" IN (0, 1)");
            });
            entity.HasKey(shift => shift.Id);
            entity.HasAlternateKey(shift => new { shift.CompanyId, shift.Id });
            entity.Property(shift => shift.OpeningCash).HasPrecision(12, 2);
            entity.Property(shift => shift.Status).HasConversion<int>().IsRequired();
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(shift => new { shift.CompanyId, shift.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(shift => shift.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PosTerminal>().WithMany()
                .HasForeignKey(shift => new { shift.CompanyId, shift.BranchId, shift.PosTerminalId })
                .HasPrincipalKey(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(shift => new { shift.CompanyId, shift.BranchId, shift.CashierId })
                .IsUnique().HasFilter("\"Status\" = 0");
            entity.HasIndex(shift => new { shift.CompanyId, shift.BranchId, shift.PosTerminalId })
                .IsUnique().HasFilter("\"Status\" = 0 AND \"PosTerminalId\" IS NOT NULL");
            entity.HasIndex(shift => new { shift.CompanyId, shift.OpenedAtUtc });
            entity.HasIndex(shift => new { shift.CompanyId, shift.BranchId, shift.PosTerminalId, shift.OpenedAtUtc });
        });
    }
}
