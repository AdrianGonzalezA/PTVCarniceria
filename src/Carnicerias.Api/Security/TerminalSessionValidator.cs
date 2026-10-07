using Carnicerias.Api.Pos;
using Carnicerias.Infrastructure;
using Carnicerias.PlatformAccess;

namespace Carnicerias.Api.Security;

public sealed record TerminalSessionValidation(bool IsValid, PosTerminal? Terminal);

public static class TerminalSessionValidator
{
    public static async Task<TerminalSessionValidation> ValidateAsync(
        UserSession session,
        HttpContext httpContext,
        PosTerminalAuthenticationService terminals,
        CancellationToken cancellationToken)
    {
        var hasCredential = httpContext.Request.Headers.ContainsKey(PosTerminalEndpoints.CredentialHeader);
        if (session.PosTerminalId is null)
            return new TerminalSessionValidation(!hasCredential, null);
        if (!hasCredential)
            return new TerminalSessionValidation(false, null);

        var terminal = await terminals.FindActiveAsync(
            httpContext.Request.Headers[PosTerminalEndpoints.CredentialHeader].ToString(), cancellationToken);
        var valid = terminal?.Id == session.PosTerminalId &&
                    (session.CompanyId is null || session.CompanyId == terminal.CompanyId) &&
                    (session.BranchId is null || session.BranchId == terminal.BranchId);
        return new TerminalSessionValidation(valid, valid ? terminal : null);
    }
}
