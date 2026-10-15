using Fsp.Orchestrator.Application.Prerequisites;

namespace Fsp.Orchestrator.Application.Deployments;

public sealed class DeploymentProfileService(
    IDeploymentProfileValidator validator,
    IDeploymentProfileStore store,
    IPrerequisiteGate prerequisiteGate,
    IArmDeploymentPreviewGateway previewGateway,
    TimeProvider timeProvider)
    : IDeploymentProfileService
{
    public async Task<DeploymentProfileDocument> SaveAsync(
        DeploymentProfile profile,
        CancellationToken cancellationToken)
    {
        var errors = validator.Validate(profile);
        if (errors.Count > 0)
        {
            throw new DeploymentProfileValidationException(errors);
        }

        return await store.SaveAsync(
            profile,
            DeploymentProfileFingerprint.Compute(profile),
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<DeploymentProfileDocument?> GetAsync(
        Guid profileId,
        CancellationToken cancellationToken) =>
        store.GetAsync(profileId, cancellationToken);

    public async Task<DeploymentPreview> PreviewAsync(
        Guid profileId,
        Guid prerequisiteRunId,
        DeploymentActor actor,
        CancellationToken cancellationToken)
    {
        var document = await store.GetAsync(profileId, cancellationToken)
            ?? throw new DeploymentProfileNotFoundException();
        var inputs = new Dictionary<string, string>(
            document.Profile.ToPrerequisiteInputs(),
            StringComparer.Ordinal)
        {
            [PrerequisiteSystemInputKeys.CurrentTenantId] = actor.TenantId,
            [PrerequisiteSystemInputKeys.CurrentObjectId] = actor.ObjectId,
        };
        var gate = await prerequisiteGate.ValidateAsync(
            prerequisiteRunId,
            "deploy",
            inputs,
            cancellationToken);
        if (!gate.Allowed)
        {
            throw new DeploymentPrerequisiteException(
                gate.Code,
                gate.Message,
                gate.BlockingCheckIds);
        }

        var result = await previewGateway.ValidateAndPreviewAsync(
            document.Profile,
            actor,
            cancellationToken);
        return await store.SavePreviewAsync(
            profileId,
            document.Fingerprint,
            prerequisiteRunId,
            result,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<DeploymentApproval> ApproveAsync(
        Guid profileId,
        Guid previewId,
        DeploymentActor actor,
        CancellationToken cancellationToken) =>
        store.ApproveAsync(
            profileId,
            previewId,
            actor,
            timeProvider.GetUtcNow(),
            cancellationToken);
}
