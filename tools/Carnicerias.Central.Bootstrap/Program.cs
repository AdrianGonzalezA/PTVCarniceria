using Carnicerias.Central.Infrastructure;

namespace Carnicerias.Central.Bootstrap;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var connectionString = Environment.GetEnvironmentVariable("CARNICERIAS_CENTRAL_CONNECTION_STRING");
            if (args is ["migrate", "--backup-file", var backupPath,
                "--backup-sha256", var backupSha256, "--apply"])
            {
                var migrations = await CentralMigrationCommand.RunAsync(
                    connectionString, backupPath, backupSha256, CancellationToken.None);
                Console.WriteLine(migrations.Count == 0
                    ? "Central schema is already up to date."
                    : $"Applied {migrations.Count} central migration(s).");
                return 0;
            }

            if (args is ["company-create", "--code", var code,
                "--name", var name, "--username", var username])
            {
                var companyId = await CentralBootstrapCommand.CreateCompanyAsync(
                    connectionString, code, name, username, CancellationToken.None);
                Console.WriteLine($"Created central company {companyId} and its first administrator.");
                return 0;
            }

            if (args is ["manufacturer-create", "--username", var manufacturerUsername])
            {
                await using var context = await CentralBootstrapCommand.OpenVerifiedContextAsync(
                    connectionString, CancellationToken.None);
                var password = CentralBootstrapCommand.ReadConfirmedPassword();
                var id = await CentralManufacturerBootstrapService.CreateAsync(context,
                    manufacturerUsername, password, new CentralPasswordHasher(6),
                    CancellationToken.None);
                Console.WriteLine($"Created central SuperAdmin {id}.");
                return 0;
            }

            if (args is ["manufacturer-password-reset", "--username", var recoveryUsername])
            {
                await using var context = await CentralBootstrapCommand.OpenVerifiedContextAsync(
                    connectionString, CancellationToken.None);
                var password = CentralBootstrapCommand.ReadConfirmedPassword();
                await CentralManufacturerBootstrapService.ResetPasswordAsync(context,
                    recoveryUsername, password, new CentralPasswordHasher(6),
                    CancellationToken.None);
                Console.WriteLine("Central SuperAdmin password reset; previous sessions revoked.");
                return 0;
            }

            if (args is ["password-reset", "--code", var resetCode,
                "--username", var resetUsername])
            {
                await CentralBootstrapCommand.ResetPasswordAsync(
                    connectionString, resetCode, resetUsername, CancellationToken.None);
                Console.WriteLine("Central password reset; previous sessions revoked.");
                return 0;
            }

            if (args is ["installation-register", "--company-code", var companyCode,
                "--installation-id", var installationIdText,
                "--remote-company-id", var remoteCompanyIdText,
                "--remote-branch-id", var remoteBranchIdText,
                "--label", var label, "--url", var destinationUrl,
                "--secret-ref", var secretReference, .. var remainder]
                && Guid.TryParse(installationIdText, out var installationId)
                && Guid.TryParse(remoteCompanyIdText, out var remoteCompanyId)
                && Guid.TryParse(remoteBranchIdText, out var remoteBranchId)
                && (remainder.Length == 0 || remainder is ["--allow-loopback-http"]))
            {
                var request = new CentralRegistrationRequest(companyCode, installationId,
                    remoteCompanyId, remoteBranchId, label, destinationUrl, secretReference);
                var registeredId = await CentralRegistrationCommand.RunAsync(connectionString,
                    request, remainder.Length == 1, CancellationToken.None);
                Console.WriteLine($"Registered central installation {registeredId}.");
                return 0;
            }

            Console.Error.WriteLine("Usage: migrate --backup-file <absolute .dump path> --backup-sha256 <SHA256> --apply");
            Console.Error.WriteLine("   or: company-create --code <code> --name <name> --username <username>");
            Console.Error.WriteLine("   or: manufacturer-create --username <username>");
            Console.Error.WriteLine("   or: manufacturer-password-reset --username <username>");
            Console.Error.WriteLine("   or: password-reset --code <code> --username <username>");
            Console.Error.WriteLine("   or: installation-register --company-code <code> --installation-id <uuid> --remote-company-id <uuid> --remote-branch-id <uuid> --label <name> --url <approved-url> --secret-ref <reference> [--allow-loopback-http]");
            return 2;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid central command input.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Central command failed ({exception.GetType().Name}).");
            return 1;
        }
    }
}
