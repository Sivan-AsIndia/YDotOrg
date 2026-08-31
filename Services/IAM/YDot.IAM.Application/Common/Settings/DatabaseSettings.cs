namespace YDot.IAM.Application.Common.Settings;

/// <summary>Bound from the DatabaseSettings section using the options pattern.</summary>
public sealed class DatabaseSettings
{
    public const string SectionName = "DatabaseSettings";

    public string ConnectionString { get; set; } = string.Empty;

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Applies pending EF migrations at startup. Convenient locally, dangerous in production.</summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;

    /// <summary>Runs the seeder at startup. The seeder is idempotent, so this is safe to leave on.</summary>
    public bool SeedOnStartup { get; set; } = true;

    /// <summary>Logs parameter values in EF diagnostics. Never enable outside development.</summary>
    public bool EnableSensitiveDataLogging { get; set; }

    public bool EnableDetailedErrors { get; set; }
}
