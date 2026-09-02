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
/// Country, state and city names, read from the IAM master catalogue over the shared database.
///
/// READ-ONLY, WITHOUT EXCEPTION. CAM never writes a country, a state or a city - it stores loose
/// ids into <c>gm_countries</c>, <c>gm_state_provinces</c> and <c>gm_cities</c>, deliberately not
/// as foreign keys, because CAM and IAM are separately deployable services that happen to share
/// a database. This class is the whole seam, and if the master catalogue ever moves it is the
/// only thing that changes.
///
/// NOTHING HERE THROWS, for the reason <see cref="FinancialDirectory"/> gives: a campaign detail
/// that cannot reach the geography tables should show its dates and its owners with the location
/// blank, not refuse to open. A missing name is visibly missing; a page that will not load tells
/// nobody anything.
///
/// NO TENANT FILTER, and that is safe here in a way it is not for people. These are reference
/// rows - Tamil Nadu is Tamil Nadu for everybody - and the ids being resolved came off a campaign
/// the caller had already been allowed to read.
/// </summary>
public sealed class GeographyDirectory(
    CampaignDbContext context,
    ILogger<GeographyDirectory> logger) : IGeographyDirectory
{
    public async Task<PlaceNames> GetPlaceNamesAsync(
        Guid countryId, Guid? stateId, Guid? cityId, CancellationToken cancellationToken)
    {
        // ONE ROUND TRIP FOR ALL THREE. The campaign detail needs country, state and city
        // together and never one without the others, so three queries would be two more than
        // the screen has any use for.
        const string Sql = """
            SELECT 'country', id, name FROM gm_countries        WHERE id = @countryId
            UNION ALL
            SELECT 'state',   id, name FROM gm_state_provinces  WHERE id = @stateId
            UNION ALL
            SELECT 'city',    id, name FROM gm_cities           WHERE id = @cityId
            """;

        string? country = null;
        string? state = null;
        string? city = null;

        try
        {
            await using var command = await CreateCommandAsync(Sql, cancellationToken);

            AddUuid(command, "countryId", countryId);
            AddUuid(command, "stateId", stateId ?? Guid.Empty);
            AddUuid(command, "cityId", cityId ?? Guid.Empty);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();

                switch (reader.GetString(0))
                {
                    case "country": country = name; break;
                    case "state": state = name; break;
                    case "city": city = name; break;
                }
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not resolve the geography names for country {CountryId}. "
                + "The affected rows will display without a location.",
                countryId);
        }

        return new PlaceNames(country, state, city);
    }

    public Task<IReadOnlyDictionary<Guid, string>> GetStateNamesAsync(
        IReadOnlyCollection<Guid> stateIds, CancellationToken cancellationToken) =>
        GetNamesAsync("gm_state_provinces", stateIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, string>> GetCityNamesAsync(
        IReadOnlyCollection<Guid> cityIds, CancellationToken cancellationToken) =>
        GetNamesAsync("gm_cities", cityIds, cancellationToken);

    /// <summary>
    /// Names by id from one master table.
    ///
    /// The table name is INTERPOLATED and the ids are PARAMETERISED, which is the only shape
    /// this may take: the two call sites above pass compile-time literals, so no caller-supplied
    /// string ever reaches the SQL text, while the ids - which do come from a request - go
    /// through a parameter.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        string table, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var names = new Dictionary<Guid, string>();

        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToArray();

        if (wanted.Length == 0)
        {
            return names;
        }

        var sql = $"SELECT id, name FROM {table} WHERE id = ANY(@ids)";

        try
        {
            await using var command = await CreateCommandAsync(sql, cancellationToken);

            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = wanted
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(1))
                {
                    names[reader.GetGuid(0)] = reader.GetString(1).Trim();
                }
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                exception,
                "Could not resolve names from {Table}. The affected rows will display without one.",
                table);
        }

        return names;
    }

    private static void AddUuid(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid) { Value = value });

    /// <summary>
    /// A command on the DbContext's own connection, inside its transaction when there is one, so
    /// a read here sees writes the same request has already made.
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
