using Carnicerias.Infrastructure;

namespace Carnicerias.ArchitectureTests;

public sealed class BranchDeviceTests
{
    [Fact]
    public void ConfiguresAnEscPosPrinterForEitherSupportedPaperWidth()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var device = new BranchDevice(companyId, branchId, "Comandera mostrador", "printer",
            "escpos", "com3", 9600, 80);
        var terminal = new PosTerminal(companyId, branchId, "Caja A");

        terminal.AssignCatalogDevices(null, device);
        Assert.Equal(device.Id, terminal.PrinterDeviceId);
        Assert.Equal(new PrinterDeviceAssignment("escpos", "COM3", 9600, 80),
            terminal.DeviceAssignments().Printer);

        device.Configure("Comandera mostrador", "printer", "escpos", "com5", 19200, 58);
        terminal.AssignCatalogDevices(null, device);
        Assert.Equal(new PrinterDeviceAssignment("escpos", "COM5", 19200, 58),
            terminal.DeviceAssignments().Printer);
    }

    [Fact]
    public void RejectsWrongBranchAndInvalidDriverParameters()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var terminal = new PosTerminal(companyId, branchId, "Caja A");
        var otherBranch = new BranchDevice(companyId, Guid.NewGuid(), "Impresora", "printer",
            "escpos", "COM3", 9600, 80);
        Assert.Throws<ArgumentException>(() => terminal.AssignCatalogDevices(null, otherBranch));
        Assert.Throws<ArgumentException>(() => new BranchDevice(companyId, branchId,
            "Impresora", "printer", "unknown-driver", "COM3", 9600, 80));
        Assert.Throws<ArgumentException>(() => new BranchDevice(companyId, branchId,
            "Impresora", "printer", "escpos", "COM3", 9600, 60));
        Assert.Throws<ArgumentException>(() => new BranchDevice(companyId, branchId,
            "Balanza", "scale", "serial-scale", "COM3", 9600, 80));
        Assert.Throws<ArgumentException>(() => new BranchDevice(companyId, branchId,
            "Impresora", "printer", "text", "COM3\n", 9600));
    }
}
