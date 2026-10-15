namespace Fsp.Orchestrator.Application.Identity;

public interface IEntraRegistrationValidator
{
    EntraRegistrationValidationResult Validate(EntraRegistrationPair registration);
}
