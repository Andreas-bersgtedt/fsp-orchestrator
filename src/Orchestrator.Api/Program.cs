using System.Text.Json;
using System.Text.Json.Serialization;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Api.Endpoints;
using Fsp.Orchestrator.Api.Hosting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = SpaWebRoot.Resolve(),
});

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "FSP Orchestrator";
});

var hostingRuntime = LoopbackHosting.Configure(builder);
builder.Services.AddSingleton(hostingRuntime);
builder.Services.AddSingleton<StartupReadiness>();
builder.Services.AddOrchestratorAuthentication(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.AllowDuplicateProperties = false;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
});

var app = builder.Build();
var readiness = app.Services.GetRequiredService<StartupReadiness>();

app.Lifetime.ApplicationStarted.Register(readiness.MarkReady);
app.Lifetime.ApplicationStopping.Register(readiness.MarkStopping);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi()
    .RequireAuthorization(OrchestratorRoles.Reader);
var api = app.MapGroup("/api")
    .RequireAuthorization(AuthorizationPolicies.Access);
api.MapHealth();
api.MapAuthentication();
api.MapMethods(
        "/{**path}",
        ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"],
        () => TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "API route not found",
            detail: "The requested API route does not exist."))
    .ExcludeFromDescription();
app.MapMethods(
        "/openapi/{**path}",
        ["GET", "HEAD"],
        () => TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "OpenAPI document not found",
            detail: "The requested OpenAPI document does not exist."))
    .RequireAuthorization(OrchestratorRoles.Reader)
    .ExcludeFromDescription();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program
{
}
