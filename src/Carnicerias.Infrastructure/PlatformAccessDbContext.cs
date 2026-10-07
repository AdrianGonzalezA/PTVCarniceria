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

    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<PriceList> PriceLists => Set<PriceList>();

    public DbSet<BranchPriceList> BranchPriceLists => Set<BranchPriceList>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();

    public DbSet<ProductCode> ProductCodes => Set<ProductCode>();

    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();

    public DbSet<SaleDraft> SaleDrafts => Set<SaleDraft>();

    public DbSet<BranchInventoryBalance> BranchInventoryBalances => Set<BranchInventoryBalance>();

    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();

    public DbSet<ConfirmedSale> ConfirmedSales => Set<ConfirmedSale>();

    public DbSet<ConfirmedSaleLine> ConfirmedSaleLines => Set<ConfirmedSaleLine>();

    public DbSet<SalePayment> SalePayments => Set<SalePayment>();

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
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("branches");
            entity.HasKey(branch => branch.Id);
            entity.Property(branch => branch.Name).HasMaxLength(200).IsRequired();
            entity.HasAlternateKey(branch => new { branch.CompanyId, branch.Id });
            entity.HasOne<Company>().WithMany().HasForeignKey(branch => branch.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosTerminal>(entity =>
        {
            entity.ToTable("pos_terminals");
            entity.HasKey(terminal => terminal.Id);
            entity.HasAlternateKey(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Id });
            entity.Property(terminal => terminal.Name).HasMaxLength(120).IsRequired();
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(terminal => new { terminal.CompanyId, terminal.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(terminal => new { terminal.CompanyId, terminal.BranchId, terminal.Name })
                .IsUnique();
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
        });

        modelBuilder.Entity<PriceList>(entity =>
        {
            entity.ToTable("price_lists", "catalog_pricing");
            entity.HasKey(list => list.Id);
            entity.HasAlternateKey(list => new { list.CompanyId, list.Id });
            entity.Property(list => list.Name).HasMaxLength(160).IsRequired();
            entity.HasOne<Company>().WithMany().HasForeignKey(list => list.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
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
            entity.ToTable("sale_drafts", "pos_sales");
            entity.HasKey(draft => draft.Id);
            entity.HasAlternateKey(draft => new { draft.CompanyId, draft.Id });
            entity.Property(draft => draft.CreatedAtUtc).IsRequired();
            entity.Property(draft => draft.UpdatedAtUtc).IsRequired();
            entity.Property(draft => draft.Status).HasConversion<int>().IsRequired();
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
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(draft => draft.CancelledByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(draft => draft.Lines).WithOne()
                .HasForeignKey(line => line.SaleDraftId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(draft => new { draft.CompanyId, draft.BranchId, draft.UserId })
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
            entity.HasOne<CatalogProduct>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.ProductId })
                .HasPrincipalKey(product => new { product.CompanyId, product.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(line => line.SaleDraftId);
        });

        modelBuilder.Entity<ConfirmedSale>(entity =>
        {
            entity.ToTable("confirmed_sales", "pos_sales", table =>
                table.HasCheckConstraint("CK_confirmed_sales_total_positive", "\"Total\" > 0"));
            entity.HasKey(sale => sale.Id);
            entity.HasAlternateKey(sale => new { sale.CompanyId, sale.Id });
            entity.Property(sale => sale.Total).HasPrecision(12, 2);
            entity.Property(sale => sale.PaymentRequestHash).HasMaxLength(64).IsRequired();
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(sale => new { sale.CompanyId, sale.BranchId })
                .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserIdentity>().WithMany().HasForeignKey(sale => sale.CashierId)
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
            entity.HasMany(sale => sale.Lines).WithOne().HasForeignKey(line => line.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(sale => sale.Payments).WithOne().HasForeignKey(payment => payment.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<CashLedgerMovement>(entity =>
        {
            entity.ToTable("cash_ledger", "payments_cash", table =>
            {
                table.HasCheckConstraint("CK_cash_ledger_amount_nonzero", "\"AmountDelta\" <> 0");
                table.HasCheckConstraint("CK_cash_ledger_kind", "\"Kind\" IN (0, 1, 2)");
            });
            entity.HasKey(movement => movement.Id);
            entity.Property(movement => movement.Method).HasConversion<int>().IsRequired();
            entity.Property(movement => movement.Kind).HasConversion<int>().IsRequired();
            entity.Property(movement => movement.AmountDelta).HasPrecision(12, 2);
            entity.HasOne<CashierShift>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.CashierShiftId })
                .HasPrincipalKey(shift => new { shift.CompanyId, shift.Id })
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
                table.HasCheckConstraint("CK_inventory_movements_kind", "\"Kind\" IN (0, 1, 2)");
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
            entity.HasIndex(shift => new { shift.CompanyId, shift.BranchId, shift.CashierId })
                .IsUnique().HasFilter("\"Status\" = 0");
        });
    }
}
