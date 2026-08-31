using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Models;

namespace YDot.PAY.Infrastructure.Persistence;

/// <summary>
/// Design-time factory, used only by <c>dotnet ef</c>.
///
/// The EF tooling has to construct a DbContext to read the model, but this one takes
/// <see cref="ITenantContext"/>, <see cref="ICurrentUser"/> and <see cref="IDateTimeProvider"/> -
/// none of which exist outside a real request. Rather than booting the whole API host just to add
/// a migration, this hands EF a set of inert stubs.
///
/// THE STUB TENANT CONTEXT REPORTS NO ORGANISATION. At design time the query filters are only
/// being read for their SHAPE - what columns they reference - and no query is ever executed, so
/// there is nothing for the stub to leak.
/// </summary>
public sealed class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        // Read from the environment where there is one, so the same command works against a
        // container. Falls back to the shared local development database - the same one IAM, DON
        // and CAM use, because all four services share it.
        var connectionString =
            Environment.GetEnvironmentVariable("YDOT_PAY_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ydotphaseupdated;Username=postgres;Password=user;Include Error Detail=true";

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history_pay"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PaymentDbContext(
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

        public bool HasTenant => false;

        public bool IsSuperAdmin => true;

        public bool IsPublicDonorContext => false;

        public Guid RequireTenantId() =>
            throw new InvalidOperationException("No organisation context exists at design time.");
    }

    private sealed class DesignTimeCurrentUser : ICurrentUser
    {
        public Guid UserId => Guid.Empty;

        public bool IsAuthenticated => false;

        public string? DisplayName => null;

        public string? Username => null;

        public string? Email => null;

        public Guid? SessionId => null;

        public IReadOnlySet<string> Permissions => new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<string> Roles => [];

        public IReadOnlyList<string> DataScopes => [];

        public bool IsSuperAdmin => false;

        public bool IsTenantAdmin => false;

        public string CorrelationId => "design-time";

        public string? IpAddress => null;

        public string? UserAgent => null;

        public string? IdempotencyKey => null;

        public bool HasPermission(string permissionCode) => false;

        public bool HasAnyPermission(params string[] permissionCodes) => false;

        public AccessScope Scope => AccessScope.Empty;
    }

    private sealed class DesignTimeClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);

        /// <summary>
        /// Never called at design time, but it has to answer something. The April default
        /// matches <c>PaymentSettings.FinancialYearStartMonth</c>.
        /// </summary>
        public string FinancialYearFor(DateTimeOffset moment)
        {
            var startYear = moment.Month >= 4 ? moment.Year : moment.Year - 1;

            return $"{startYear}-{(startYear + 1) % 100:D2}";
        }
    }
}
