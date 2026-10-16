using System.Net.Mail;
using System.Text;

namespace Carnicerias.Bootstrap;

public sealed record CompanyAdminBootstrapArguments(Guid CompanyId, string Username, string Email)
{
    public const string Usage =
        "Uso: create-company-admin --company-id <id> --username <usuario> --email <correo>. La contraseña se ingresa en consola.";

    public static CompanyAdminBootstrapArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count != 7 || args[0] != "create-company-admin")
            throw new ArgumentException(Usage);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            if (args[index] is not ("--company-id" or "--username" or "--email") ||
                !values.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException(Usage);
        }

        if (!values.TryGetValue("--company-id", out var companyValue) ||
            !Guid.TryParse(companyValue, out var companyId) || companyId == Guid.Empty ||
            !values.TryGetValue("--username", out var username) ||
            !values.TryGetValue("--email", out var email))
            throw new ArgumentException(Usage);

        username = username.Trim().Normalize(NormalizationForm.FormKC);
        email = email.Trim().Normalize(NormalizationForm.FormKC);
        if (username.Length is < 1 or > 100 || username.Contains('@') ||
            email.Length is < 3 or > 320 || !MailAddress.TryCreate(email, out var address) ||
            !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(Usage);

        return new CompanyAdminBootstrapArguments(companyId, username, email);
    }
}
