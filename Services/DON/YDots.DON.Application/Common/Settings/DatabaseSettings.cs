namespace YDots.DON.Application.Common.Settings;

/// <summary>Bound from the DatabaseSettings section of appsettings.json using the options pattern.</summary>
public sealed class DatabaseSettings
{
    public const string SectionName = "DatabaseSettings";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Apply pending EF Core migrations automatically when the API starts.</summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;

    /// <summary>Insert the reference and demonstration data when the API starts.</summary>
    public bool SeedOnStartup { get; set; } = true;

    public int CommandTimeoutSeconds { get; set; } = 60;
}
