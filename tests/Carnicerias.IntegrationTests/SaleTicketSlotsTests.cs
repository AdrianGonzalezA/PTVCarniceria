using Carnicerias.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Carnicerias.IntegrationTests;

public sealed class SaleTicketSlotsTests
{
    [Fact]
    public void DraftsKeepTheirTicketSlot()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var legacy = new SaleDraft(companyId, branchId, cashierId, listId, now);
        var drafts = Enum.GetValues<SaleTicketSlot>().Select(slot =>
            new SaleDraft(companyId, branchId, cashierId, listId, now, ticketSlot: slot)).ToArray();

        Assert.Equal(SaleTicketSlot.A, legacy.TicketSlot);
        Assert.Equal([SaleTicketSlot.A, SaleTicketSlot.B, SaleTicketSlot.C, SaleTicketSlot.D],
            drafts.Select(draft => draft.TicketSlot));
    }

    [Fact]
    public void DatabaseModelAllowsOneActiveDraftPerTicketSlot()
    {
        var options = new DbContextOptionsBuilder<PlatformAccessDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=55433;Database=carnicerias_test_visual;Username=postgres")
            .Options;
        using var db = new PlatformAccessDbContext(options);
        var draft = db.Model.FindEntityType(typeof(SaleDraft));

        Assert.NotNull(draft);
        Assert.Contains(draft.GetIndexes(), index => index.IsUnique &&
            index.GetFilter() == "\"Status\" = 0" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(SaleDraft.CompanyId), nameof(SaleDraft.BranchId),
                nameof(SaleDraft.UserId), nameof(SaleDraft.TicketSlot)]));
    }
}
