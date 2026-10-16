namespace Norvia.DriverApp.Application.Reset;

/// <summary>The setting that allows a reset. It is off unless the estate switches it on.</summary>
public interface IResetSwitch
{
    bool IsOn { get; }
}
