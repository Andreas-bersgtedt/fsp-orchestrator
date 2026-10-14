using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

namespace Fsp.Orchestrator.Api.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static EntraAuthenticationConfiguration AddOrchestratorAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var entra = EntraAuthenticationConfiguration.From(configuration);
        services.AddSingleton(entra);

        if (entra.IsConfigured)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(
                    configuration.GetSection(EntraAuthenticationConfiguration.SectionName),
                    jwtBearerScheme: JwtBearerDefaults.AuthenticationScheme);
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters.NameClaimType = "name";
                    options.TokenValidationParameters.RoleClaimType = "roles";
                    options.TokenValidationParameters.ValidateAudience = true;
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidateLifetime = true;
                    options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(1);
                    options.TokenValidationParameters.ValidAudiences =
                    [
                        entra.ClientId,
                        $"api://{entra.ClientId}",
                    ];
                });
        }
        else
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddScheme<AuthenticationSchemeOptions, SetupRequiredAuthenticationHandler>(
                    JwtBearerDefaults.AuthenticationScheme,
                    _ => { });
        }

        services.AddSingleton<IAuthorizationHandler, TenantRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, ScopeRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, AuthorizedClientRequirementHandler>();
        services.AddAuthorization(
            options => AuthorizationPolicies.Add(options, entra));

        return entra;
    }
}
