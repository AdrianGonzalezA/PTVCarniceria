using System.Text;

namespace Carnicerias.Bootstrap;

public static class BootstrapPasswordPolicy
{
    private const int MinimumLength = 12;
    private const int MaximumUtf8Bytes = 1024;

    public static bool IsValid(string password) =>
        !string.IsNullOrWhiteSpace(password) &&
        password.Length >= MinimumLength &&
        Encoding.UTF8.GetByteCount(password) <= MaximumUtf8Bytes;
}
