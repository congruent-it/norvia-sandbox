namespace Norvia.Tms.Infrastructure.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Applies EF Core migrations when the TMS starts. Only for local runs: deployed environments migrate in a
    /// deployment step (coding guidelines §5).
    /// </summary>
    public bool MigrateOnStartup { get; init; }
}
