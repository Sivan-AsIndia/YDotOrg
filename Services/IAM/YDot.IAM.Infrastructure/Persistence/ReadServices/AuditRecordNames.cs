using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace YDot.IAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Puts names where the audit trail stored ids.
///
/// WHY. An audit writer records what it has to hand, and what it has is ids:
/// <c>{ "previousTenantId": "9fb11890-…", "roleIds": ["…", "…"] }</c>. That is right for the
/// row - an id survives a rename - and useless to the person reading "What changed" on the trail,
/// who cannot tell one GUID from the next. So the ids are resolved to the records' names when the
/// trail is READ, which also means a renamed record shows its current name.
///
/// ONE LOOKUP PER TABLE PER PAGE, not per row: every id on the page is collected first, and each
/// table is asked only for the ids still unresolved, so the common case - users, organisations,
/// roles - is settled in the first few queries and the rest are skipped.
///
/// AN ID NOTHING KNOWS is still not printed. A hyphenated one is usually a record another service
/// owns, or one since deleted, and becomes <see cref="UnknownRecord"/>; a 32-character one is a
/// correlation id, and becomes the short reference support quotes (see <see cref="ShortReference"/>).
/// </summary>
internal static partial class AuditRecordNames
{
    public const string UnknownRecord = "Record not available";

    /// <summary>Any GUID inside a string: 8-4-4-4-12, or the 32-hex form correlation ids use.</summary>
    [GeneratedRegex(
        @"\{?\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b\}?|\b[0-9a-fA-F]{32}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedGuid();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The first eight characters of a correlation id, upper-case - "7F3A09C2". What a person
    /// quotes to support; the log is searched by prefix.
    /// </summary>
    public static string ShortReference(string? correlationId)
    {
        var compact = new string((correlationId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());

        return compact.Length == 0 ? string.Empty : compact[..Math.Min(8, compact.Length)].ToUpperInvariant();
    }

    /// <summary>Every GUID mentioned in these texts - JSON metadata and free-text reasons alike.</summary>
    public static HashSet<Guid> IdsIn(IEnumerable<string?> texts)
    {
        var ids = new HashSet<Guid>();

        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            foreach (Match match in EmbeddedGuid().Matches(text))
            {
                if (Guid.TryParse(match.Value, out var id) && id != Guid.Empty)
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// The same text with each GUID replaced by the name it resolves to.
    ///
    /// JSON IS REWRITTEN AS JSON, value by value, so the screen can still parse and lay it out;
    /// anything that is not valid JSON is treated as plain text.
    /// </summary>
    public static string? Rewrite(string? text, IReadOnlyDictionary<Guid, string> names)
    {
        if (string.IsNullOrEmpty(text) || !EmbeddedGuid().IsMatch(text))
        {
            return text;
        }

        var trimmed = text.TrimStart();

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                var node = JsonNode.Parse(text);

                if (node is not null)
                {
                    RewriteValues(node, names);
                    return node.ToJsonString(SerializerOptions);
                }
            }
            catch (JsonException)
            {
                // Not JSON after all - fall through to plain text.
            }
        }

        return ReplaceIn(text, names);
    }

    private static void RewriteValues(JsonNode node, IReadOnlyDictionary<Guid, string> names)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                {
                    if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        obj[key] = ReplaceIn(text, names);
                    }
                    else if (obj[key] is { } child)
                    {
                        RewriteValues(child, names);
                    }
                }

                break;

            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        array[index] = ReplaceIn(text, names);
                    }
                    else if (array[index] is { } child)
                    {
                        RewriteValues(child, names);
                    }
                }

                break;
        }
    }

    private static string ReplaceIn(string text, IReadOnlyDictionary<Guid, string> names) =>
        EmbeddedGuid().Replace(text, match =>
            Guid.TryParse(match.Value, out var id) && names.TryGetValue(id, out var name)
                ? name
                : match.Value.Contains('-') ? UnknownRecord : ShortReference(match.Value));

    /// <summary>
    /// The names of the records behind these ids, from every table the trail can refer to.
    ///
    /// IGNORING THE QUERY FILTERS, because the row being read is already scoped to the caller's
    /// Organisation and the ids in it can legitimately point outside it - a platform
    /// administrator, the Organisation a SuperAdmin stepped out of.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        IamDbContext context, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        var pending = new HashSet<Guid>(ids);

        async Task Take<T>(IQueryable<T> source, Expression<Func<T, NamedRow>> select)
            where T : class
        {
            if (pending.Count == 0)
            {
                return;
            }

            var wanted = pending.ToList();
            var rows = await source.Select(select)
                .Where(row => wanted.Contains(row.Id))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.Name) && pending.Remove(row.Id))
                {
                    names[row.Id] = row.Name;
                }
            }
        }

        // Most likely first: the trail is mostly about people, Organisations and roles.
        await Take(context.Users.IgnoreQueryFilters(),
            item => new NamedRow { Id = item.Id, Name = item.DisplayName != "" ? item.DisplayName : item.Code });
        await Take(context.Tenants.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.Roles.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name ?? item.Code });
        await Take(context.Departments.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.OrganisationUnits.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.BusinessUnits.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.MenuDefinitions.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.Permissions.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Code });
        await Take(context.AccessRequests.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.RequestNumber });
        await Take(context.AccessReviewCampaigns.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.BulkOperations.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.OperationNumber });
        await Take(context.PaymentGatewayConfigurations.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.DisplayName });
        await Take(context.Countries.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.StateProvinces.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.Cities.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.Currencies.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Code + " - " + item.Name });
        await Take(context.TimeZones.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });
        await Take(context.Languages.IgnoreQueryFilters(), item => new NamedRow { Id = item.Id, Name = item.Name });

        return names;
    }

    private sealed class NamedRow
    {
        public Guid Id { get; init; }

        public string? Name { get; init; }
    }
}
