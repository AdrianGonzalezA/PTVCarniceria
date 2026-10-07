using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carnicerias.IntegrationTests;

[Collection("DatabaseIntegration")]
public sealed class PosTerminalMigrationTests
{
    [PostgreSqlFact]
    [Trait("Category", "Integration")]
    public async Task DatabaseRejectsTwoOpenShiftsAtOneTerminal()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        Assert.StartsWith("carnicerias_test_", databaseName, StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;

        await using var db = new PlatformAccessDbContext(options);
        await db.Database.MigrateAsync();
        try
        {
            var company = new Company("Caja única");
            var branch = new Branch(company.Id, "Centro");
            var first = UserIdentity.Create("first-cashier", "first@example.test", "test-only-hash");
            var second = UserIdentity.Create("second-cashier", "second@example.test", "test-only-hash");
            var terminal = new PosTerminal(company.Id, branch.Id, "Caja 1");
            db.AddRange(company, branch, first, second, terminal);
            await db.SaveChangesAsync();

            db.CashierShifts.Add(new CashierShift(company.Id, branch.Id, first.Id,
                0m, DateTimeOffset.UtcNow, terminal.Id));
            await db.SaveChangesAsync();
            db.CashierShifts.Add(new CashierShift(company.Id, branch.Id, second.Id,
                0m, DateTimeOffset.UtcNow, terminal.Id));

            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation,
                Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        finally
        {
            await db.Database.MigrateAsync("0");
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "Integration")]
    public async Task ActiveDraftWithoutOpenShiftStopsMigrationWithoutDiscardingIt()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        Assert.StartsWith("carnicerias_test_", databaseName, StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;

        await using var db = new PlatformAccessDbContext(options);
        await db.Database.MigrateAsync("20261006191441_AddCashLedgerSaleReference");
        try
        {
            var company = new Company("Migración bloqueada");
            var branch = new Branch(company.Id, "Centro");
            var user = UserIdentity.Create("cashier-orphan", "cashier-orphan@example.test", "test-only-hash");
            var priceList = new PriceList(company.Id, "Mostrador");
            db.AddRange(company, branch, user, priceList);
            await db.SaveChangesAsync();
            var draftId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pos_sales.sale_drafts
                    ("Id", "CompanyId", "BranchId", "UserId", "PriceListId", "CreatedAtUtc", "UpdatedAtUtc", "Status")
                VALUES ({draftId}, {company.Id}, {branch.Id}, {user.Id}, {priceList.Id}, {now}, {now}, {0})
                """);

            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());

            Assert.Contains("Active legacy sale draft has no open cashier shift", error.MessageText);
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM pos_sales.sale_drafts WHERE \"Status\" = 0")
                .SingleAsync());
            Assert.DoesNotContain(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddPosTerminals", StringComparison.Ordinal));
        }
        finally
        {
            await db.Database.MigrateAsync("0");
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "Integration")]
    public async Task ExistingSaleAndCashRecordsMoveToHistoricalTerminalWithoutChangingAmounts()
    {
        var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_TEST_CONNECTION_STRING")!;
        var databaseName = new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database;
        Assert.StartsWith("carnicerias_test_", databaseName, StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql(connectionString).Options;

        await using var db = new PlatformAccessDbContext(options);
        await db.Database.MigrateAsync("20261006191441_AddCashLedgerSaleReference");
        try
        {
            var company = new Company("Migración de caja");
            var branch = new Branch(company.Id, "Centro");
            var user = UserIdentity.Create("cashier-migration", "cashier-migration@example.test", "test-only-hash");
            var activeUser = UserIdentity.Create("cashier-active", "cashier-active@example.test", "test-only-hash");
            var priceList = new PriceList(company.Id, "Mostrador");
            var category = new ProductCategory(company.Id, "Carnes");
            var product = new CatalogProduct(company.Id, category.Id, "ASADO", "Asado", "kg",
                ProductSaleMode.Weight, 50m);
            var inventory = new BranchInventoryBalance(company.Id, branch.Id, product.Id);
            inventory.SetQuantities(3m, 1m);
            db.AddRange(company, branch, user, activeUser, priceList, category, product, inventory);
            await db.SaveChangesAsync();

            var shiftId = Guid.NewGuid();
            var draftId = Guid.NewGuid();
            var saleId = Guid.NewGuid();
            var movementId = Guid.NewGuid();
            var activeShiftId = Guid.NewGuid();
            var activeDraftId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO payments_cash.cashier_shifts
                    ("Id", "CompanyId", "BranchId", "CashierId", "OpeningCash", "OpenedAtUtc", "ClosedAtUtc", "Status")
                VALUES ({shiftId}, {company.Id}, {branch.Id}, {user.Id}, {250m}, {now.AddHours(-2)}, {now}, {1})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pos_sales.sale_drafts
                    ("Id", "CompanyId", "BranchId", "UserId", "PriceListId", "CreatedAtUtc", "UpdatedAtUtc", "Status", "ConfirmedAtUtc", "ConfirmedSaleId")
                VALUES ({draftId}, {company.Id}, {branch.Id}, {user.Id}, {priceList.Id}, {now.AddHours(-1)}, {now}, {2}, {now}, {saleId})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pos_sales.confirmed_sales
                    ("Id", "CompanyId", "BranchId", "CashierId", "CashierShiftId", "SourceDraftId", "PriceListId", "Total", "PaymentRequestHash", "ConfirmedAtUtc")
                VALUES ({saleId}, {company.Id}, {branch.Id}, {user.Id}, {shiftId}, {draftId}, {priceList.Id}, {100m}, {new string('a', 64)}, {now})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO payments_cash.cash_ledger
                    ("Id", "CompanyId", "BranchId", "CashierShiftId", "CashierId", "OperationId", "Method", "Kind", "AmountDelta", "CreatedAtUtc", "SaleId")
                VALUES ({movementId}, {company.Id}, {branch.Id}, {shiftId}, {user.Id}, {Guid.NewGuid()}, {0}, {1}, {100m}, {now}, {saleId})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO payments_cash.cashier_shifts
                    ("Id", "CompanyId", "BranchId", "CashierId", "OpeningCash", "OpenedAtUtc", "Status")
                VALUES ({activeShiftId}, {company.Id}, {branch.Id}, {activeUser.Id}, {0m}, {now}, {0})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pos_sales.sale_drafts
                    ("Id", "CompanyId", "BranchId", "UserId", "PriceListId", "CreatedAtUtc", "UpdatedAtUtc", "Status")
                VALUES ({activeDraftId}, {company.Id}, {branch.Id}, {activeUser.Id}, {priceList.Id}, {now}, {now}, {0})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pos_sales.sale_draft_lines
                    ("Id", "SaleDraftId", "CompanyId", "ProductId", "ProductCode", "ProductName", "Unit", "SaleMode", "Quantity", "UnitPrice")
                VALUES ({Guid.NewGuid()}, {activeDraftId}, {company.Id}, {product.Id}, {"ASADO"}, {"Asado"}, {"kg"}, {0}, {1m}, {100m})
                """);

            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();

            var terminal = await db.PosTerminals.SingleAsync();
            Assert.Equal(company.Id, terminal.CompanyId);
            Assert.Equal(branch.Id, terminal.BranchId);
            Assert.True(terminal.IsHistorical);
            Assert.False(terminal.IsActive);
            Assert.Equal(2, await db.CashierShifts.CountAsync());
            Assert.Equal(2, await db.SaleDrafts.CountAsync());
            Assert.All(await db.CashierShifts.ToListAsync(), shift => Assert.Equal(terminal.Id, shift.PosTerminalId));
            Assert.All(await db.SaleDrafts.ToListAsync(), draft => Assert.Equal(terminal.Id, draft.PosTerminalId));
            Assert.Equal(shiftId, (await db.SaleDrafts.SingleAsync(draft => draft.Id == draftId)).CashierShiftId);
            Assert.Equal(activeShiftId, (await db.SaleDrafts.SingleAsync(draft => draft.Id == activeDraftId)).CashierShiftId);
            Assert.Equal(terminal.Id, (await db.ConfirmedSales.SingleAsync()).PosTerminalId);
            var movement = await db.CashLedgerMovements.SingleAsync();
            Assert.Equal(terminal.Id, movement.PosTerminalId);
            Assert.Equal(100m, movement.AmountDelta);
            Assert.Equal(100m, (await db.ConfirmedSales.SingleAsync()).Total);
            Assert.Equal(1m, (await db.BranchInventoryBalances.SingleAsync()).Reserved);

            await db.Database.MigrateAsync("20261006191441_AddCashLedgerSaleReference");
            Assert.Equal(100m, await db.Database.SqlQueryRaw<decimal>(
                "SELECT \"Total\" AS \"Value\" FROM pos_sales.confirmed_sales").SingleAsync());
            Assert.Equal(100m, await db.Database.SqlQueryRaw<decimal>(
                "SELECT \"AmountDelta\" AS \"Value\" FROM payments_cash.cash_ledger").SingleAsync());
            Assert.Equal(1m, await db.Database.SqlQueryRaw<decimal>(
                "SELECT \"Reserved\" AS \"Value\" FROM inventory.branch_inventory").SingleAsync());
        }
        finally
        {
            await db.Database.MigrateAsync("0");
        }
    }
}
