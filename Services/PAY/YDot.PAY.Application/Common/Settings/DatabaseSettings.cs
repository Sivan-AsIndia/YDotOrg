namespace YDot.PAY.Application.Common.Settings;

/// <summary>
/// Everything about the database connection, bound through the option pattern.
///
/// THE CONNECTION STRING IS EMPTY HERE and supplied from
/// <c>DatabaseSettings__ConnectionString</c>, so a password never sits in a committed file.
/// </summary>
public sealed class DatabaseSettings
{
    public const string SectionName = "DatabaseSettings";

    public string ConnectionString { get; set; } = string.Empty;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool ApplyMigrationsOnStartup { get; set; } = true;

    public bool SeedOnStartup { get; set; } = true;

    /// <summary>
    /// Logs parameter VALUES.
    ///
    /// NEVER ENABLE THIS OUTSIDE A DEVELOPER MACHINE, and it matters more here than in the other
    /// services: the parameters passing through this context include donor names, e-mail
    /// addresses, tax identifiers and amounts.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; }

    public bool EnableDetailedErrors { get; set; }
}
