using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fsp.Orchestrator.IntegrationTests;

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "IntegrationTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Authenticated", out var authenticated)
            || authenticated != "true")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("oid", "00000000-0000-0000-0000-000000000111"),
            new("name", "Integration Test Operator"),
            new("preferred_username", "operator@example.test"),
            new("tid", Header("X-Test-Tenant")),
            new("azp", Header("X-Test-Client")),
            new("scp", Header("X-Test-Scopes")),
        };
        claims.AddRange(
            Header("X-Test-Roles")
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim("roles", role)));

        var identity = new ClaimsIdentity(
            claims,
            Scheme,
            nameType: "name",
            roleType: "roles");
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string Header(string name)
    {
        return Request.Headers[name].ToString();
    }
}
