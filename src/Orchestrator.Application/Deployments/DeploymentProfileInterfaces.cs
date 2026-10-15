namespace Fsp.Orchestrator.Application.Deployments;

public interface IDeploymentProfileValidator
{
    IReadOnlyList<DeploymentProfileValidationError> Validate(
        DeploymentProfile profile);
}

public interface IDeploymentProfileStore
{
    Task<IReadOnlyList<DeploymentProfileDocument>> ListAsync(
        CancellationToken cancellationToken);

    Task<DeploymentProfileDocument> SaveAsync(
        DeploymentProfile profile,
        string fingerprint,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken);

    Task<DeploymentProfileDocument?> GetAsync(
        Guid profileId,
        CancellationToken cancellationToken);

    Task<DeploymentPreview?> GetPreviewAsync(
        Guid profileId,
        CancellationToken cancellationToken);

    Task<DeploymentApproval?> GetApprovalAsync(
        Guid profileId,
        CancellationToken cancellationToken);

    Task<DeploymentPreview> SavePreviewAsync(
        Guid profileId,
        string expectedProfileFingerprint,
        Guid prerequisiteRunId,
        ArmDeploymentPreview preview,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    Task<DeploymentApproval> ApproveAsync(
        Guid profileId,
        Guid previewId,
        DeploymentActor actor,
        DateTimeOffset approvedAt,
        CancellationToken cancellationToken);
}

public interface IArmDeploymentPreviewGateway
{
    Task<ArmDeploymentPreview> ValidateAndPreviewAsync(
        DeploymentProfile profile,
        DeploymentActor actor,
        CancellationToken cancellationToken);
}

public interface IDeploymentProfileService
{
    Task<DeploymentProfileDocument> SaveAsync(
        DeploymentProfile profile,
        CancellationToken cancellationToken);

    Task<DeploymentProfileDocument?> GetAsync(
        Guid profileId,
        CancellationToken cancellationToken);

    Task<DeploymentPreview> PreviewAsync(
        Guid profileId,
        Guid prerequisiteRunId,
        DeploymentActor actor,
        CancellationToken cancellationToken);

    Task<DeploymentApproval> ApproveAsync(
        Guid profileId,
        Guid previewId,
        DeploymentActor actor,
        CancellationToken cancellationToken);
}

public sealed class DeploymentProfileValidationException(
    IReadOnlyList<DeploymentProfileValidationError> errors)
    : Exception("The deployment profile is invalid.")
{
    public IReadOnlyList<DeploymentProfileValidationError> Errors { get; } =
        errors;
}

public sealed class DeploymentProfileNotFoundException()
    : Exception("The deployment profile was not found.");

public sealed class DeploymentProfileConflictException(string code)
    : Exception("The deployment profile changed. Run validation and preview again.")
{
    public string Code { get; } = code;
}

public sealed class DeploymentPrerequisiteException(
    string code,
    string message,
    IReadOnlyList<string> blockingCheckIds)
    : Exception(message)
{
    public string Code { get; } = code;

    public IReadOnlyList<string> BlockingCheckIds { get; } = blockingCheckIds;
}

public sealed class ArmDeploymentPreviewException(
    string code,
    string message,
    int statusCode)
    : Exception(message)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}
