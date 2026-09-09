using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Infrastructure.Persistence;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// The Organisation's own host, read from IAM's domain table over the shared database.
///
/// THE SAME SEAM <c>TenantResolutionMiddleware</c> ALREADY READS, and deliberately so: the
/// middleware turns a host into an Organisation for an arriving donor, and this turns an
/// Organisation back into a host for a document leaving us. One table answers both, so a link we
/// send always resolves to the Organisation we sent it for.
///
/// ONLY A USABLE HOST IS RETURNED - active and verified. An unverified custom domain is one the
/// charity has claimed and not yet proven; sending a donor there produces a dead link, and the
/// platform subdomain that certainly works is sitting in the same table.
/// </summary>
public sealed class TenantHostDirectory(PaymentDbContext context, ILogger<TenantHostDirectory> logger)
    : ITenantHostDirectory
{
    public async Task<string?> GetPrimaryHostAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        // THE PRIMARY ROW FIRST, then any other usable one. Exactly one row per Organisation
        // carries is_primary, and it is the host the platform is meant to build links on; the
        // fallback exists so an Organisation whose primary flag was never set still gets a
        // working link rather than the platform host.
        const string Sql = """
            SELECT host_name
            FROM iam_tenant_domains
            WHERE tenant_id = @tenant_id AND is_active AND is_verified
            ORDER BY is_primary DESC, host_name
            LIMIT 1
            """;

        try
        {
            await using var command = await CreateCommandAsync(Sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenantId });

            var host = await command.ExecuteScalarAsync(cancellationToken) as string;

            return string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        }
        catch (NpgsqlException exception)
        {
            // NOT RETHROWN. A receipt whose link points at the platform host is worse than one
            // that points at the charity's own, and better than no receipt at all - the document
            // is a tax record first and a link second.
            logger.LogError(
                exception,
                "Could not resolve the host for organisation {TenantId}. Links will be built on "
                + "the platform host instead.",
                tenantId);

            return null;
        }
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var command = new NpgsqlCommand(sql, connection);

        if (context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction transaction)
        {
            command.Transaction = transaction;
        }

        return command;
    }
}
