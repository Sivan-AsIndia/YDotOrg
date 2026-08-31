using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence;

/// <summary>
/// Design-time factory, used only by <c>dotnet ef</c>.
///
/// The EF tooling has to construct a DbContext to read the model, but this one takes
/// <see cref="ITenantContext"/>, <see cref="ICurrentUser"/> and
/// <see cref="IDateTimeProvider"/> — none of which exist outside a real request. Rather than
/// booting the whole API host just to add a migration, this hands EF a set of inert stubs.
///
/// THE STUB TENANT CONTEXT REPORTS GLOBAL SCOPE ON PURPOSE. At design time the query filters
/// are only being read for their shape, and a null Organisation would make every filter
/// evaluate against nothing. Reporting global scope keeps the model complete, and since no
/// query is ever executed here it cannot leak anything.
/// </summary>
public sealed class IamDbContextFactory : IDesignTimeDbContextFactory<IamDbContext>
{
    public IamDbContext CreateDbContext(string[] args)
    {
        // Read the connection string from the environment when there is one, so the same
        // command works against a container. Falls back to the local development database.
        var connectionString =
            Environment.GetEnvironmentVariable("YDOT_IAM_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ydotphaseupdated;Username=postgres;Password=user;Include Error Detail=true";

        var options = new DbContextOptionsBuilder<IamDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history_iam"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IamDbContext(
            options,
            new DesignTimeTenantContext(),
            new DesignTimeCurrentUser(),
            new DesignTimeClock());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;

        public Guid BusinessUnitId => Guid.Empty;

        public string? TenantCode => null;

        public string? TenantName => null;

        public TenantStatus? TenantStatus => null;

        public AccessScopeType Scope => AccessScopeType.Global;

        public bool IsSuperAdmin => true;

        public bool IsTenantMode => false;

        public bool HasTenant => false;

        public string? HostName => null;

        public bool IsPlatformHost => true;

        /// <summary>True so the model builds with complete filters. Nothing is queried here.</summary>
        public bool IsGlobalQueryScope => true;

        public Guid RequireTenantId() =>
            throw new InvalidOperationException("No tenant context exists at design time.");
    }

    private sealed class DesignTimeCurrentUser : ICurrentUser
    {
        public Guid UserId => Guid.Empty;

        public bool IsAuthenticated => false;

        public string? UserCode => null;

        public string? DisplayName => null;

        public string? Username => null;

        public string? Email => null;

        public Guid? SessionId => null;

        public IReadOnlySet<string> Permissions => new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<string> Roles => [];

        public IReadOnlyList<string> DataScopes => [];

        public PrivilegeLevel PrivilegeLevel => PrivilegeLevel.Standard;

        public bool IsSuperAdmin => false;

        public bool IsTenantAdmin => false;

        public bool MfaCompleted => false;

        public TokenType TokenType => TokenType.Access;

        public string? SecurityStamp => null;

        public Guid? DepartmentId => null;

        public Guid? OrganisationUnitId => null;

        public string CorrelationId => "design-time";

        public string? IpAddress => null;

        public string? UserAgent => null;

        public ClientType ClientType => ClientType.Unknown;

        public string? Browser => null;

        public string? OperatingSystem => null;

        public string? DeviceIdentifier => null;

        public string? IdempotencyKey => null;

        public bool HasPermission(string permissionCode) => false;

        public bool HasAllPermissions(params string[] permissionCodes) => false;

        public bool HasAnyPermission(params string[] permissionCodes) => false;

        public AccessScope Scope => AccessScope.Empty;
    }

    private sealed class DesignTimeClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
