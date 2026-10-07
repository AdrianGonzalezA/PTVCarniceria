namespace Carnicerias.Bootstrap;

public sealed record BootstrapAdminArguments(
    string Username,
    string Email,
    string CompanyName,
    string BranchName);

public static class BootstrapArgumentParser
{
    private const string Usage =
        "Usage: Carnicerias.Bootstrap create-admin --username <name> --email <email> [--company <name>] [--branch <name>]. The password is entered interactively.";

    public static BootstrapAdminArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] != "create-admin" || (args.Count - 1) % 2 != 0)
        {
            throw new ArgumentException(Usage);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            if (option is not ("--username" or "--email" or "--company" or "--branch") ||
                !values.TryAdd(option, args[index + 1]))
            {
                throw new ArgumentException(Usage);
            }
        }

        if (!values.TryGetValue("--username", out var username) || string.IsNullOrWhiteSpace(username) ||
            !values.TryGetValue("--email", out var email) || string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(Usage);
        }

        return new BootstrapAdminArguments(
            username,
            email,
            ReadOptionalValue(values, "--company", "Empresa principal"),
            ReadOptionalValue(values, "--branch", "Sucursal principal"));
    }

    private static string ReadOptionalValue(
        Dictionary<string, string> values,
        string key,
        string defaultValue)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(Usage);
        }

        return value.Trim();
    }
}
