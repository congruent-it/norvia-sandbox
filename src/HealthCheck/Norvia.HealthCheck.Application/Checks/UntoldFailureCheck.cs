namespace Norvia.HealthCheck.Application.Checks;

/// <summary>
/// A Check that pairs a failure with a missing alert: something failed and nobody was told. The alert Checks find the missing alert
/// whether or not anything failed; these find the ones that did. They are not toggles of their own, so they share the toggle of the alert
/// Check whose absence they pair with.
/// </summary>
public abstract class UntoldFailureCheck : Check
{
    public override Severity Severity => Severity.Critical;
}
