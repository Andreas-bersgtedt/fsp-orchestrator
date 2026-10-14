using System.Text.Json;
using System.Text.Json.Serialization;
using Fsp.Orchestrator.Api.Endpoints;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});

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

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();
app.MapHealth();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program
{
}
