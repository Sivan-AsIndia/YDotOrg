using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using YDots.DON.Infrastructure.Persistence;

namespace YDots.DON.Infrastructure.Services;

/// <summary>
/// The Organisation's people, read from the identity tables over the shared database.
///
/// WHY THIS EXISTS. DON has no user table - IAM owns those - so the owner selector was built from
/// the names already recorded on leads and assignments. That works once work is flowing and fails
/// completely before it is: a brand-new Organisation has no leads, so it has no owner names, so
/// the Assignment Board offers NOBODY TO ASSIGN TO, so no lead can be given an owner, so there is
/// still no owner name tomorrow. The list could only ever contain people who were already on it.
///
/// READ-ONLY, WITHOUT EXCEPTION. DON never creates, renames or deactivates a user. If identity
/// ever moves to a database of its own, this one class is what changes.
///
/// NOTHING HERE THROWS. If the identity tables cannot be reached the selector falls back to the
/// names already known from leads, which is exactly what it showed before this existed.
/// </summary>
public sealed class PeopleDirectory(
    DonDbContext context,
    ILogger<PeopleDirectory> logger)
{
    /// <summary>
    /// Who may be given a lead.
    ///
    /// Active accounts only. Somebody suspended, deactivated or withdrawn must not appear in an
    /// owner selector: handing them work would mean the lead sits with a person who cannot sign in
    /// to act on it, and nobody would notice until the donor did.
    /// </summary>
    public async Task<IReadOnlyList<(Guid UserId, string Name)>> GetAssignableAsync(
        Guid organisationId, CancellationToken cancellationToken)
    {
        var people = new List<(Guid, string)>();

        if (organisationId == Guid.Empty)
        {
            return people;
        }

        const string Sql = """
            SELECT id, display_name
            FROM iam_users
            WHERE tenant_id = @organisationId
              AND status = 'Active'
            ORDER BY display_name
            """;

        var connection = context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;

        try
        {
            if (opened)
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = Sql;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "organisationId";
            parameter.Value = organisationId;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                people.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }
        catch (Exception exception) when (exception is PostgresException
                                              or NpgsqlException
                                              or InvalidOperationException)
        {
            logger.LogWarning(
                exception,
                "Could not read the people list for organisation {OrganisationId}. "
                + "The owner selector will show only the owners already known from leads.",
                organisationId);
        }
        finally
        {
            if (opened && connection.State == System.Data.ConnectionState.Open)
            {
                await context.Database.CloseConnectionAsync();
            }
        }

        return people;
    }
}
