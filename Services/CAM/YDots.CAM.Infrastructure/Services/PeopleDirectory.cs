using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using YDots.CAM.Application.Common.Abstractions.Services;
using YDots.CAM.Infrastructure.Persistence;

namespace YDots.CAM.Infrastructure.Services;

/// <summary>
/// Users, read from the identity tables over the shared database.
///
/// READ-ONLY, WITHOUT EXCEPTION, and scoped to one organisation on every call. CAM never writes a
/// user and never reads one belonging to another tenant. The whole surface is a single existence
/// query, which is what makes a seam across a shared database defensible here.
///
/// THIS ONE DOES THROW, unlike <see cref="FinancialDirectory"/>, and the difference is deliberate.
/// A missing income figure is cosmetic, so that class swallows its errors and shows a blank. This
/// class answers "does this owner exist", and a validator that cannot reach identity must not
/// quietly decide the answer is yes - that would let through exactly the record it is there to
/// stop. Failing loudly is the safe direction for a check whose whole job is to refuse.
/// </summary>
public sealed class PeopleDirectory(
    CampaignDbContext context,
    ILogger<PeopleDirectory> logger) : IPeopleDirectory
{
    public async Task<IReadOnlySet<Guid>> GetExistingUserIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var found = new HashSet<Guid>();

        if (tenantId == Guid.Empty || userIds.Count == 0)
        {
            return found;
        }

        // TENANT FILTERED IN THE QUERY, not afterwards. An owner id from another organisation must
        // read as "does not exist" here, so that a campaign can never be pointed at a stranger.
        //
        // Invited users are accepted. Somebody who has been invited but has not yet accepted is a
        // real person the organisation has named, and refusing to let them own a campaign would
        // stop an administrator setting the work up before the person's first sign-in.
        const string Sql = """
            SELECT id
            FROM iam_users
            WHERE tenant_id = @tenantId
              AND id = ANY(@ids)
            """;

        try
        {
            await using var command = await CreateCommandAsync(Sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("tenantId", NpgsqlDbType.Uuid)
            {
                Value = tenantId
            });

            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = userIds.Distinct().ToArray()
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                found.Add(reader.GetGuid(0));
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not resolve owner ids against the identity tables for organisation {TenantId}.",
                tenantId);

            throw;
        }

        return found;
    }

    /// <summary>
    /// A command on the DbContext's own connection, inside its transaction when there is one, so
    /// this check sees writes the same request has already made.
    /// </summary>
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
