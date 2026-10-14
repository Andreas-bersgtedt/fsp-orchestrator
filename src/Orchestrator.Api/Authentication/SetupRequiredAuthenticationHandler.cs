using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Fsp.Orchestrator.Api.Authentication;

public sealed class SetupRequiredAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        return Task.FromResult(
            Request.Headers.ContainsKey("Authorization")
                ? AuthenticateResult.Fail(
                    "Microsoft Entra authentication is not configured.")
                : AuthenticateResult.NoResult());
    }
}
