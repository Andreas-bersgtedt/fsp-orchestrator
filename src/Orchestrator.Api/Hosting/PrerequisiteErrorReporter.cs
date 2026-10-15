using Fsp.Orchestrator.Application.Prerequisites;

namespace Fsp.Orchestrator.Api.Hosting;

public sealed partial class PrerequisiteErrorReporter(
    ILogger<PrerequisiteErrorReporter> logger)
    : IPrerequisiteErrorReporter
{
    public void Report(string checkId, string exceptionType)
    {
        LogCheckError(logger, checkId, exceptionType);
    }

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Error,
        Message = "Prerequisite check {CheckId} failed with {ExceptionType}")]
    private static partial void LogCheckError(
        ILogger logger,
        string checkId,
        string exceptionType);
}
