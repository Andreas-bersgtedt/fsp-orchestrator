using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Api.Azure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Fsp.Orchestrator.Api.Endpoints;

public static class AzureEndpoints
{
    internal static IResult SignInRequiredProblem(HttpContext httpContext) =>
        SignInRequired(httpContext);

    public static RouteGroupBuilder MapAzure(this RouteGroupBuilder endpoints)
    {
        endpoints.MapPost(
                "/azure/session",
                EstablishSessionAsync)
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("EstablishAzureSession")
            .WithSummary("Hand off a delegated ARM token")
            .WithDescription(
                "Validates signature, audience, lifetime, client, tenant, and "
                + "object ID before retaining the token in process memory.")
            .Produces<AzureSessionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        endpoints.MapDelete(
                "/azure/session",
                (IAzureTokenSessionStore sessions, ClaimsPrincipal user) =>
                {
                    sessions.Remove(user);
                    return TypedResults.NoContent();
                })
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("ClearAzureSession")
            .WithSummary("Clear the delegated ARM token")
            .WithDescription("Removes the current operator's in-memory ARM token.")
            .Produces(StatusCodes.Status204NoContent);

        endpoints.MapGet(
                "/azure/session",
                IResult (
                    IAzureTokenSessionStore sessions,
                    ClaimsPrincipal user,
                    HttpContext httpContext) =>
                {
                    try
                    {
                        var session = sessions.Retrieve(user);
                        return TypedResults.Ok(
                            new AzureSessionResponse(
                                session.TenantId,
                                session.ObjectId,
                                session.ExpiresAt));
                    }
                    catch (AzureSignInRequiredException)
                    {
                        return (IResult)SignInRequired(httpContext);
                    }
                })
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("GetAzureSession")
            .WithSummary("Get delegated Azure session status")
            .WithDescription(
                "Returns identity and expiry metadata without returning the ARM token.")
            .Produces<AzureSessionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        endpoints.MapGet(
                "/azure/subscriptions",
                ListSubscriptionsAsync)
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("ListAzureSubscriptions")
            .WithSummary("List allowed Azure subscriptions")
            .WithDescription(
                "Lists subscriptions visible to the delegated operator token.")
            .Produces<AzureSubscriptionListResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status502BadGateway);

        endpoints.MapGet(
                "/azure/resources",
                ListResourcesAsync)
            .RequireAuthorization(OrchestratorRoles.DeploymentAdmin)
            .WithName("ListAzureResources")
            .WithSummary("List resources in an allowed Azure scope")
            .WithDescription(
                "Validates subscription visibility before returning stable ARM "
                + "resource IDs and API metadata.")
            .Produces<AzureResourceListResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status502BadGateway);

        return endpoints;
    }

    private static async Task<IResult> EstablishSessionAsync(
        AzureTokenHandoffRequest request,
        IAzureDelegatedTokenValidator validator,
        IAzureTokenSessionStore sessions,
        EntraAuthenticationConfiguration entra,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        try
        {
            var identity = await validator.ValidateAsync(
                request.AccessToken,
                user,
                entra.SpaClientId,
                cancellationToken);
            sessions.Store(identity, request.AccessToken);
            return TypedResults.Ok(
                new AzureSessionResponse(
                    identity.TenantId,
                    identity.ObjectId,
                    identity.ExpiresAt));
        }
        catch (SecurityTokenException)
        {
            return Problem(
                StatusCodes.Status401Unauthorized,
                "Azure token rejected",
                "The ARM token did not match the signed-in Orchestrator identity.",
                "azure.token_invalid");
        }
    }

    private static async Task<IResult> ListSubscriptionsAsync(
        IAzureTokenSessionStore sessions,
        IAzureResourceDiscovery discovery,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = sessions.Retrieve(user);
            var subscriptions = await discovery.ListSubscriptionsAsync(
                session,
                cancellationToken);
            return TypedResults.Ok(
                new AzureSubscriptionListResponse(
                    subscriptions.Select(
                            subscription => new AzureSubscriptionResponse(
                                subscription.SubscriptionId,
                                subscription.DisplayName,
                                subscription.State,
                                subscription.ResourceId,
                                subscription.ApiVersion))
                        .ToArray(),
                    session.ExpiresAt));
        }
        catch (AzureSignInRequiredException)
        {
            sessions.Remove(user);
            return SignInRequired(httpContext);
        }
        catch (AzureScopeDeniedException)
        {
            return ScopeDenied();
        }
        catch (HttpRequestException)
        {
            return ArmUnavailable();
        }
        catch (JsonException)
        {
            return ArmInvalidResponse();
        }
    }

    private static async Task<IResult> ListResourcesAsync(
        Guid subscriptionId,
        string? resourceGroup,
        IAzureTokenSessionStore sessions,
        IAzureResourceDiscovery discovery,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (subscriptionId == Guid.Empty)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Invalid Azure scope",
                "A valid subscription ID is required.",
                "azure.subscription_invalid");
        }

        try
        {
            var session = sessions.Retrieve(user);
            var resources = await discovery.ListResourcesAsync(
                session,
                subscriptionId,
                resourceGroup,
                cancellationToken);
            return TypedResults.Ok(
                new AzureResourceListResponse(
                    subscriptionId,
                    resourceGroup,
                    resources.Select(
                            resource => new AzureResourceResponse(
                                resource.ResourceId,
                                resource.Name,
                                resource.Type,
                                resource.Location,
                                resource.ApiVersion))
                        .ToArray(),
                    session.ExpiresAt));
        }
        catch (ArgumentException)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Invalid Azure scope",
                "The resource group name is invalid.",
                "azure.resource_group_invalid");
        }
        catch (AzureSignInRequiredException)
        {
            sessions.Remove(user);
            return SignInRequired(httpContext);
        }
        catch (AzureScopeDeniedException)
        {
            return ScopeDenied();
        }
        catch (HttpRequestException)
        {
            return ArmUnavailable();
        }
        catch (JsonException)
        {
            return ArmInvalidResponse();
        }
    }

    private static ProblemHttpResult SignInRequired(HttpContext httpContext)
    {
        httpContext.Response.Headers.WWWAuthenticate =
            "Bearer error=\"invalid_token\", "
            + "error_description=\"Azure sign-in required\"";
        return Problem(
            StatusCodes.Status401Unauthorized,
            "Azure sign-in required",
            "Acquire a new delegated ARM token and resume the operation.",
            "azure.sign_in_required");
    }

    private static ProblemHttpResult ScopeDenied() =>
        Problem(
            StatusCodes.Status403Forbidden,
            "Azure scope denied",
            "The signed-in operator cannot access the requested Azure scope.",
            "azure.scope_denied");

    private static ProblemHttpResult ArmUnavailable() =>
        Problem(
            StatusCodes.Status502BadGateway,
            "Azure Resource Manager unavailable",
            "Azure Resource Manager could not be reached.",
            "azure.arm_unavailable");

    private static ProblemHttpResult ArmInvalidResponse() =>
        Problem(
            StatusCodes.Status502BadGateway,
            "Azure Resource Manager response invalid",
            "Azure Resource Manager returned an invalid discovery response.",
            "azure.arm_response_invalid");

    private static ProblemHttpResult Problem(
        int status,
        string title,
        string detail,
        string code) =>
        TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
            });
}

/// <summary>Payload containing a delegated Azure Resource Manager token.</summary>
public sealed record AzureTokenHandoffRequest
{
    [Required, MinLength(100), MaxLength(65_536)]
    public required string AccessToken { get; init; }
}

/// <summary>Represents the validated in-memory Azure session.</summary>
public sealed record AzureSessionResponse(
    string TenantId,
    string ObjectId,
    DateTimeOffset ExpiresAt);

/// <summary>Represents subscriptions visible to the delegated operator.</summary>
public sealed record AzureSubscriptionListResponse(
    IReadOnlyList<AzureSubscriptionResponse> Subscriptions,
    DateTimeOffset TokenExpiresAt);

/// <summary>Represents stable Azure subscription metadata.</summary>
public sealed record AzureSubscriptionResponse(
    Guid SubscriptionId,
    string DisplayName,
    string State,
    string ResourceId,
    string ApiVersion);

/// <summary>Represents resources returned for an allowed Azure scope.</summary>
public sealed record AzureResourceListResponse(
    Guid SubscriptionId,
    string? ResourceGroup,
    IReadOnlyList<AzureResourceResponse> Resources,
    DateTimeOffset TokenExpiresAt);

/// <summary>Represents stable Azure resource metadata.</summary>
public sealed record AzureResourceResponse(
    string ResourceId,
    string Name,
    string Type,
    string? Location,
    string ApiVersion);
