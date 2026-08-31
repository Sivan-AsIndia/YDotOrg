namespace YDots.CAM.Application.Common.Settings;

/// <summary>
/// Everything about the database connection, bound through the option pattern rather than read
/// from <c>IConfiguration</c> at the point of use.
///
/// THE CONNECTION STRING IS DELIBERATELY EMPTY IN appsettings.json. It is supplied by
/// <c>DatabaseSettings__ConnectionString</c> from the environment, so a password never sits in
/// a file that is committed.
/// </summary>
public sealed class DatabaseSettings
{
    public const string SectionName = "DatabaseSettings";

    public string ConnectionString { get; set; } = string.Empty;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool ApplyMigrationsOnStartup { get; set; } = true;

    public bool SeedOnStartup { get; set; } = true;

    /// <summary>Logs parameter VALUES. Never enable it outside a developer machine.</summary>
    public bool EnableSensitiveDataLogging { get; set; }

    public bool EnableDetailedErrors { get; set; }
}
