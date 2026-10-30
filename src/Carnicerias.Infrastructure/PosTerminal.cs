using System.Text.RegularExpressions;

namespace Carnicerias.Infrastructure;

public sealed record SerialDeviceAssignment(string Port, int BaudRate);
public sealed record PrinterDeviceAssignment(string Kind, string Port, int BaudRate,
    int? PaperWidthMm = null);
public sealed record PosTerminalDevices(SerialDeviceAssignment? Scale, PrinterDeviceAssignment? Printer);

public sealed class PosTerminal
{
    private PosTerminal() => Name = string.Empty;

    public PosTerminal(Guid companyId, Guid branchId, string name, bool isHistorical = false)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Company and branch ids are required.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Terminal name must be between 1 and 120 characters.", nameof(name));

        Id = Guid.NewGuid();
        CompanyId = companyId;
        BranchId = branchId;
        Name = name.Trim();
        IsActive = !isHistorical;
        IsHistorical = isHistorical;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsHistorical { get; private set; }

    public string? CredentialHash { get; private set; }
    public string? ScalePort { get; private set; }
    public int? ScaleBaudRate { get; private set; }
    public string? PrinterKind { get; private set; }
    public string? PrinterPort { get; private set; }
    public int? PrinterBaudRate { get; private set; }
    public int? PrinterPaperWidthMm { get; private set; }
    public Guid? ScaleDeviceId { get; private set; }
    public Guid? PrinterDeviceId { get; private set; }

    public PosTerminalDevices DeviceAssignments() => new(
        ScalePort is null ? null : new SerialDeviceAssignment(ScalePort, ScaleBaudRate!.Value),
        PrinterPort is null ? null : new PrinterDeviceAssignment(
            PrinterKind!, PrinterPort, PrinterBaudRate!.Value, PrinterPaperWidthMm));

    public void AssignCatalogDevices(BranchDevice? scale, BranchDevice? printer)
    {
        if (scale is not null && (scale.CompanyId != CompanyId || scale.BranchId != BranchId ||
            scale.Type != "scale" || !scale.IsActive) ||
            printer is not null && (printer.CompanyId != CompanyId || printer.BranchId != BranchId ||
            printer.Type != "printer" || !printer.IsActive))
            throw new ArgumentException("Device must be active and belong to this branch.");
        ConfigureDevices(new PosTerminalDevices(scale?.ScaleAssignment(), printer?.PrinterAssignment()));
        ScaleDeviceId = scale?.Id;
        PrinterDeviceId = printer?.Id;
    }

    public void ConfigureDevices(PosTerminalDevices devices)
    {
        if (IsHistorical) throw new InvalidOperationException("Historical terminals cannot use devices.");
        ArgumentNullException.ThrowIfNull(devices);
        var scale = devices.Scale;
        var printer = devices.Printer;
        if (scale is not null && !ValidSerial(scale.Port, scale.BaudRate))
            throw new ArgumentException("Invalid scale serial settings.", nameof(devices));
        if (printer is not null &&
            (printer.Kind is not ("text" or "hasar" or "escpos") ||
             !ValidSerial(printer.Port, printer.BaudRate) ||
             (printer.Kind == "hasar" && printer.BaudRate != 9600) ||
             (printer.Kind == "escpos" ? printer.PaperWidthMm is not (58 or 80)
                : printer.PaperWidthMm is not null)))
            throw new ArgumentException("Invalid printer serial settings.", nameof(devices));
        if (scale is not null && printer is not null &&
            string.Equals(scale.Port, printer.Port, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Scale and printer cannot use the same port.", nameof(devices));

        ScalePort = scale?.Port.ToUpperInvariant();
        ScaleBaudRate = scale?.BaudRate;
        PrinterKind = printer?.Kind;
        PrinterPort = printer?.Port.ToUpperInvariant();
        PrinterBaudRate = printer?.BaudRate;
        PrinterPaperWidthMm = printer?.PaperWidthMm;
        ScaleDeviceId = null;
        PrinterDeviceId = null;
    }

    private static bool ValidSerial(string? port, int baudRate) =>
        port is not null && Regex.IsMatch(port, "\\ACOM[1-9][0-9]{0,2}\\z", RegexOptions.IgnoreCase) &&
        baudRate is 1200 or 2400 or 4800 or 9600 or 19200 or 38400 or 57600 or 115200;

    public void Rename(string name)
    {
        if (IsHistorical) throw new InvalidOperationException("Historical terminals cannot be renamed.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Terminal name must be between 1 and 120 characters.", nameof(name));
        Name = name.Trim();
    }

    public void AssignCredentialHash(string hash)
    {
        if (IsHistorical)
            throw new InvalidOperationException("Historical terminals cannot be activated.");
        if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))
            throw new ArgumentException("A SHA-256 credential hash is required.", nameof(hash));

        CredentialHash = hash.ToLowerInvariant();
    }

    public void Deactivate() => IsActive = false;

    public void ReactivateWithCredentialHash(string hash)
    {
        AssignCredentialHash(hash);
        IsActive = true;
    }
}
