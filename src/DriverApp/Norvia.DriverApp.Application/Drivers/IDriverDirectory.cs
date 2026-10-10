namespace Norvia.DriverApp.Application.Drivers;

/// <summary>The Drivers the Driver app signs in as. The seed file names them; the Driver app never asks the TMS.</summary>
public interface IDriverDirectory
{
    IReadOnlyList<DriverView> All { get; }
}
