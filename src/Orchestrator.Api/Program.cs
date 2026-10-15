using System.Text.Json;
using System.Text.Json.Serialization;
using Fsp.Orchestrator.Api.Authentication;
using Fsp.Orchestrator.Api.Endpoints;
using Fsp.Orchestrator.Api.Hosting;
using Fsp.Orchestrator.Application.Identity;
using Fsp.Orchestrator.Application.Prerequisites;
using Fsp.Orchestrator.Infrastructure.Persistence;

var applicationArgs = await ProcessRestartBootstrap.PrepareAsync(args);
if (applicationArgs is null)
{
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = applicationArgs,
    WebRootPath = SpaWebRoot.Resolve(),
});

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "FSP Orchestrator";
});

var hostingRuntime = LoopbackHosting.Configure(builder);
builder.Services.AddSingleton(hostingRuntime);
builder.Services.AddSingleton<StartupReadiness>();
builder.Services.AddSingleton<IIdentityConfigurationStore, IdentityConfigurationStore>();
builder.Services.AddSingleton<IApplicationRestartScheduler, ApplicationRestartScheduler>();
builder.Services.AddOrchestratorPersistence(
    Path.Combine(hostingRuntime.DataDirectory, "orchestrator.db"));
builder.Services.AddHostedService<DatabaseMigrationHostedService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPrerequisiteEvidenceSanitizer, PrerequisiteEvidenceSanitizer>();
builder.Services.AddSingleton<IPrerequisiteErrorReporter, PrerequisiteErrorReporter>();
builder.Services.AddSingleton<IPrerequisiteEngine, PrerequisiteEngine>();
builder.Services.AddSingleton<IPrerequisiteGate, PrerequisiteGate>();
builder.Services.AddSingleton<IPrerequisiteRunCoordinator, PrerequisiteRunCoordinator>();
builder.Services.AddSingleton<IOpenIdConfigurationResolver, OpenIdConfigurationResolver>();
builder.Services.AddSingleton<ICandidateApiTokenValidator, CandidateApiTokenValidator>();
builder.Services.AddSingleton<IEntraRegistrationValidator, EntraRegistrationValidator>();
builder.Services.AddOrchestratorAuthentication(builder.Configuration);
builder.Services.AddHttpClient(nameof(LocalPrerequisiteProbe));
builder.Services.AddSingleton<ILocalPrerequisiteProbe, LocalPrerequisiteProbe>();
foreach (var definition in LocalPrerequisiteCatalog.Definitions)
{
    builder.Services.AddSingleton<IPrerequisiteCheck>(
        provider => new LocalPrerequisiteCheck(
            definition,
            provider.GetRequiredService<ILocalPrerequisiteProbe>()));
}

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
api.MapEntraRegistrations();
api.MapDatabase();
api.MapPrerequisites();
app.MapBootstrap();
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
