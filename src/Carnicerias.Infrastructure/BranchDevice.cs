using System.Text.RegularExpressions;

namespace Carnicerias.Infrastructure;

public sealed class BranchDevice
{
    private BranchDevice() => Name = Type = Driver = Port = string.Empty;

    public BranchDevice(Guid companyId, Guid branchId, string name, string type,
        string driver, string port, int baudRate, int? paperWidthMm = null)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Company and branch are required.");
        Id = Guid.NewGuid();
        Name = Type = Driver = Port = string.Empty;
        CompanyId = companyId;
        BranchId = branchId;
        IsActive = true;
        Configure(name, type, driver, port, baudRate, paperWidthMm);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Name { get; private set; }
    public string Type { get; private set; }
    public string Driver { get; private set; }
    public string Port { get; private set; }
    public int BaudRate { get; private set; }
    public int? PaperWidthMm { get; private set; }
    public bool IsActive { get; private set; }

    public void Configure(string name, string type, string driver, string port,
        int baudRate, int? paperWidthMm = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 ||
            port is null || !Regex.IsMatch(port, "\\ACOM[1-9][0-9]{0,2}\\z", RegexOptions.IgnoreCase) ||
            baudRate is not (1200 or 2400 or 4800 or 9600 or 19200 or 38400 or 57600 or 115200) ||
            (type, driver) is not (("scale", "serial-scale") or ("printer", "text") or
                ("printer", "hasar") or ("printer", "escpos")) ||
            (driver == "hasar" && baudRate != 9600) ||
            (driver == "escpos" ? paperWidthMm is not (58 or 80) : paperWidthMm is not null))
            throw new ArgumentException("Invalid branch device configuration.");

        Name = name.Trim();
        Type = type;
        Driver = driver;
        Port = port.ToUpperInvariant();
        BaudRate = baudRate;
        PaperWidthMm = paperWidthMm;
    }

    public void SetActive(bool active) => IsActive = active;

    public SerialDeviceAssignment ScaleAssignment() =>
        Type == "scale" && IsActive
            ? new SerialDeviceAssignment(Port, BaudRate)
            : throw new InvalidOperationException("Active scale required.");

    public PrinterDeviceAssignment PrinterAssignment() =>
        Type == "printer" && IsActive
            ? new PrinterDeviceAssignment(Driver, Port, BaudRate, PaperWidthMm)
            : throw new InvalidOperationException("Active printer required.");
}
