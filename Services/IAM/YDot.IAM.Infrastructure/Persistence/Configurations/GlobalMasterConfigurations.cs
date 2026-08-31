using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// The five global master tables, migrated in from the standalone GlobalMaster service.
///
/// THEY KEEP THE <c>gm_</c> PREFIX. IAM owns <c>iam_*</c> and DON owns <c>don_*</c> in the
/// shared database; the masters were already <c>gm_*</c> in their own database and there is
/// no reason to rename them just because the code that serves them moved. The prefix still
/// says which module a table belongs to, which is the only thing it was ever for.
///
/// EVERY UNIQUE INDEX IS SCOPED ON <c>tenant_key</c>, NOT ON <c>tenant_id</c>, and that is
/// the single most important line in this file. TenantId is NULLABLE for a platform row, and
/// PostgreSQL treats two NULLs as distinct inside a unique index - so a unique index on
/// (tenant_id, code) would happily accept the platform catalogue holding IN twice. TenantKey
/// mirrors null onto <c>Guid.Empty</c> precisely so the platform rows form a real, comparable
/// group, and indexing on it is what actually makes the constraint bite.
/// </summary>
public sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("gm_countries");

        builder.HasKey(country => country.Id);

        // One code per scope. TEN001 may define a private IN alongside the platform's IN, but
        // never two of its own. See the class comment for why this is tenant_key.
        builder.HasIndex(country => new { country.TenantKey, country.Code })
            .HasDatabaseName("ix_gm_countries_scope_code")
            .IsUnique();

        builder.HasIndex(country => new { country.TenantKey, country.Iso2 })
            .HasDatabaseName("ix_gm_countries_scope_iso2")
            .IsUnique();

        // The picker's ordering, and the only index the country list actually reads.
        builder.HasIndex(country => new { country.Status, country.SortOrder })
            .HasDatabaseName("ix_gm_countries_status_order");

        builder.Property(country => country.Code).HasMaxLength(50).IsRequired();
        builder.Property(country => country.Name).HasMaxLength(150).IsRequired();
        builder.Property(country => country.OfficialName).HasMaxLength(200);
        builder.Property(country => country.Iso2).HasMaxLength(2).IsRequired().IsFixedLength();
        builder.Property(country => country.Iso3).HasMaxLength(3).IsFixedLength();
        builder.Property(country => country.NumericCode).HasMaxLength(10);
        builder.Property(country => country.DefaultCurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(country => country.PostalCodePattern).HasMaxLength(200);
        builder.Property(country => country.PhoneCountryCode).HasMaxLength(10);
        builder.Property(country => country.Notes).HasMaxLength(1000);

        // Enums as TEXT, matching every other table in the model. A country's region reads as
        // "Asia" in a database console rather than as a 0 nobody can decode.
        builder.Property(country => country.Region).HasConversion<string>().HasMaxLength(40);
        builder.Property(country => country.Status).HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(country => country.Version).IsConcurrencyToken();

        // THE CURRENCY LINK IS OPTIONAL AND RESTRICTS ON DELETE. Optional because a country may
        // be seeded before its currency is, and because a tenant-added country legitimately has
        // no default; restricting because a currency that thirty countries default to is not one
        // anybody should be able to remove with a single DELETE.
        //
        // Configured with a navigationless WithMany: Currency has no Countries collection, and
        // adding one would invite somebody to load every country to render a currency row.
        builder.HasOne(country => country.DefaultCurrency)
            .WithMany()
            .HasForeignKey(country => country.DefaultCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>States, provinces and union territories.</summary>
public sealed class StateProvinceConfiguration : IEntityTypeConfiguration<StateProvince>
{
    public void Configure(EntityTypeBuilder<StateProvince> builder)
    {
        builder.ToTable("gm_state_provinces");

        builder.HasKey(state => state.Id);

        builder.HasIndex(state => new { state.TenantKey, state.Code })
            .HasDatabaseName("ix_gm_state_provinces_scope_code")
            .IsUnique();

        // The cascading picker's index: "every state in this country, in order".
        builder.HasIndex(state => new { state.CountryId, state.Status, state.SortOrder })
            .HasDatabaseName("ix_gm_state_provinces_country_status");

        builder.Property(state => state.Code).HasMaxLength(50).IsRequired();
        builder.Property(state => state.Name).HasMaxLength(150).IsRequired();
        builder.Property(state => state.DisplayName).HasMaxLength(150);
        builder.Property(state => state.OtherJurisdictionType).HasMaxLength(100);
        builder.Property(state => state.GstStateCode).HasMaxLength(10);
        builder.Property(state => state.StateTaxJurisdictionCode).HasMaxLength(50);
        builder.Property(state => state.PostalCodePattern).HasMaxLength(200);
        builder.Property(state => state.AddressFormatHint).HasMaxLength(300);
        builder.Property(state => state.Notes).HasMaxLength(1000);

        builder.Property(state => state.JurisdictionType)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(state => state.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(state => state.Version).IsConcurrencyToken();

        // RESTRICT, not CASCADE. Deleting a country that still has states beneath it would
        // silently destroy them along with every address pointing at them; the handler already
        // refuses it with a countable reason, and this is the backstop for anything that
        // reaches the database another way.
        builder.HasOne(state => state.Country)
            .WithMany(country => country.StateProvinces)
            .HasForeignKey(state => state.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        // SET NULL rather than RESTRICT, and the asymmetry is deliberate. A state's default
        // time zone is a CONVENIENCE - it pre-fills a field - so losing it degrades the form
        // rather than corrupting anything. Blocking a time-zone deletion over a default nobody
        // depends on would be the wrong trade.
        builder.HasOne(state => state.DefaultTimeZone)
            .WithMany(zone => zone.StateProvinces)
            .HasForeignKey(state => state.DefaultTimeZoneId)
            .OnDelete(DeleteBehavior.SetNull);

        // The free-text description belongs to Other and to nothing else. Enforced in the
        // database as well as in the mapper, because a row that contradicts itself here is one
        // the UI would render as nonsense.
        builder.ToTable(table =>
            table.HasCheckConstraint(
                "ck_gm_state_provinces_other_jurisdiction",
                "jurisdiction_type = 'Other' OR other_jurisdiction_type IS NULL"));
    }
}

/// <summary>Cities, towns and villages.</summary>
public sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("gm_cities");

        builder.HasKey(city => city.Id);

        builder.HasIndex(city => new { city.TenantKey, city.Code })
            .HasDatabaseName("ix_gm_cities_scope_code")
            .IsUnique();

        builder.HasIndex(city => new { city.StateProvinceId, city.Status, city.SortOrder })
            .HasDatabaseName("ix_gm_cities_state_status");

        // The denormalised country column exists to serve country-level reads without a join,
        // so it needs its own index or it does not serve them.
        builder.HasIndex(city => city.CountryId)
            .HasDatabaseName("ix_gm_cities_country");

        builder.Property(city => city.Code).HasMaxLength(50).IsRequired();
        builder.Property(city => city.Name).HasMaxLength(150).IsRequired();
        builder.Property(city => city.DisplayName).HasMaxLength(150);
        builder.Property(city => city.DefaultPostalCodePattern).HasMaxLength(200);
        builder.Property(city => city.Notes).HasMaxLength(1000);

        builder.Property(city => city.Status).HasConversion<string>().HasMaxLength(40).IsRequired();

        // Nine and six is enough for roughly ten centimetres, which is far finer than any
        // city centroid needs and avoids the rounding surprises of a float.
        builder.Property(city => city.Latitude).HasPrecision(9, 6);
        builder.Property(city => city.Longitude).HasPrecision(9, 6);

        builder.Property(city => city.Version).IsConcurrencyToken();

        builder.HasOne(city => city.StateProvince)
            .WithMany(state => state.Cities)
            .HasForeignKey(city => city.StateProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        // The second relationship to the same row set that StateProvince already reaches. It
        // is what makes the denormalised column a real foreign key rather than a loose Guid,
        // so a city can never name a country that does not exist.
        builder.HasOne(city => city.Country)
            .WithMany(country => country.Cities)
            .HasForeignKey(city => city.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Both coordinates or neither, and each inside its real range. The same rule the
        // GeoCoordinateValue enforces on the way in, restated where nothing can bypass it -
        // an import or a manual fix applied straight to the table included.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_gm_cities_coordinates_paired",
                "(latitude IS NULL) = (longitude IS NULL)");

            table.HasCheckConstraint(
                "ck_gm_cities_coordinate_range",
                "latitude IS NULL OR (latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180)");
        });
    }
}

/// <summary>Currencies.</summary>
public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("gm_currencies");

        builder.HasKey(currency => currency.Id);

        builder.HasIndex(currency => new { currency.TenantKey, currency.Code })
            .HasDatabaseName("ix_gm_currencies_scope_code")
            .IsUnique();

        builder.HasIndex(currency => new { currency.Status, currency.SortOrder })
            .HasDatabaseName("ix_gm_currencies_status_order");

        builder.Property(currency => currency.Code).HasMaxLength(3).IsRequired().IsFixedLength();
        builder.Property(currency => currency.Name).HasMaxLength(150).IsRequired();
        builder.Property(currency => currency.Symbol).HasMaxLength(10);
        builder.Property(currency => currency.DisplayFormat).HasMaxLength(50);
        builder.Property(currency => currency.MinorUnitName).HasMaxLength(50);
        builder.Property(currency => currency.Notes).HasMaxLength(1000);

        builder.Property(currency => currency.CurrencyType)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(currency => currency.SymbolPosition)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(currency => currency.RoundingMode)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(currency => currency.Status)
            .HasConversion<string>().HasMaxLength(40).IsRequired();

        // The step is a MONEY-SHAPED value, so it gets money's precision rather than the
        // default. 18,8 covers a satoshi at one end and a large fiat step at the other.
        builder.Property(currency => currency.RoundingStep).HasPrecision(18, 8);

        builder.Property(currency => currency.Version).IsConcurrencyToken();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_gm_currencies_decimal_places",
                "decimal_places BETWEEN 0 AND 8");

            table.HasCheckConstraint(
                "ck_gm_currencies_rounding_step",
                "rounding_step IS NULL OR rounding_step > 0");
        });
    }
}

/// <summary>Time zones.</summary>
public sealed class TimeZoneDefinitionConfiguration : IEntityTypeConfiguration<TimeZoneDefinition>
{
    public void Configure(EntityTypeBuilder<TimeZoneDefinition> builder)
    {
        builder.ToTable("gm_time_zones");

        builder.HasKey(zone => zone.Id);

        builder.HasIndex(zone => new { zone.TenantKey, zone.Code })
            .HasDatabaseName("ix_gm_time_zones_scope_code")
            .IsUnique();

        // The IANA key is unique in its own right, separately from the derived Code. Both
        // constraints exist because both values are looked up: the key by an integration, the
        // code by anything following the platform-wide master convention.
        builder.HasIndex(zone => new { zone.TenantKey, zone.IanaKey })
            .HasDatabaseName("ix_gm_time_zones_scope_iana")
            .IsUnique();

        // Zone pickers are ordered by offset, never alphabetically - four hundred names sorted
        // by first letter is a list nobody can use.
        builder.HasIndex(zone => new { zone.Status, zone.StandardUtcOffsetMinutes })
            .HasDatabaseName("ix_gm_time_zones_status_offset");

        builder.Property(zone => zone.Code).HasMaxLength(100).IsRequired();
        builder.Property(zone => zone.IanaKey).HasMaxLength(100).IsRequired();
        builder.Property(zone => zone.Name).HasMaxLength(150).IsRequired();
        builder.Property(zone => zone.ShortName).HasMaxLength(10);
        builder.Property(zone => zone.DaylightSavingRuleNote).HasMaxLength(500);
        builder.Property(zone => zone.Notes).HasMaxLength(1000);

        builder.Property(zone => zone.Status).HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(zone => zone.Version).IsConcurrencyToken();

        builder.ToTable(table =>
        {
            // The real-world extremes: Baker Island at -12:00, Kiritimati at +14:00.
            table.HasCheckConstraint(
                "ck_gm_time_zones_offset_range",
                "standard_utc_offset_minutes BETWEEN -720 AND 840");

            // A zone that does not observe daylight saving has no rule to describe.
            table.HasCheckConstraint(
                "ck_gm_time_zones_dst_note",
                "supports_daylight_saving OR daylight_saving_rule_note IS NULL");
        });
    }
}

/// <summary>
/// The country-to-time-zone link table.
///
/// THE UNIQUE INDEX IS THE WHOLE POINT: (tenant_key, country_id, time_zone_id) stops the same
/// zone being mapped to the same country twice, which is what would otherwise show a person
/// "Eastern Time" twice in one dropdown. Scoped on tenant_key rather than tenant_id for the
/// reason set out at the top of this file — two NULLs are distinct inside a PostgreSQL unique
/// index, so a tenant_id-scoped constraint would not bite on the platform rows at all.
///
/// BOTH FOREIGN KEYS RESTRICT. Cascading from a country would silently drop its zone mappings
/// the moment somebody deleted it, and cascading from a zone would do the same in reverse; the
/// delete handlers already refuse to remove a row anything still points at, and this is the
/// database-level backstop for that.
/// </summary>
public sealed class CountryTimeZoneConfiguration : IEntityTypeConfiguration<CountryTimeZone>
{
    public void Configure(EntityTypeBuilder<CountryTimeZone> builder)
    {
        builder.ToTable("gm_country_time_zones");

        builder.HasKey(link => link.Id);

        builder.HasIndex(link => new { link.TenantKey, link.CountryId, link.TimeZoneId })
            .HasDatabaseName("ix_gm_country_time_zones_scope_pair")
            .IsUnique();

        // The read path: "give me this country's zones, primary first". Covered end to end so
        // a cascading picker costs one index scan rather than a sort over the whole table.
        builder.HasIndex(link => new { link.CountryId, link.IsPrimary, link.SortOrder })
            .HasDatabaseName("ix_gm_country_time_zones_country_order");

        builder.HasIndex(link => link.TimeZoneId)
            .HasDatabaseName("ix_gm_country_time_zones_zone");

        builder.Property(link => link.Version).IsConcurrencyToken();

        builder.HasOne(link => link.Country)
            .WithMany(country => country.CountryTimeZones)
            .HasForeignKey(link => link.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(link => link.TimeZone)
            .WithMany(zone => zone.CountryTimeZones)
            .HasForeignKey(link => link.TimeZoneId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// The Languages table.
///
/// TWO UNIQUE INDEXES, FOR THE SAME REASON <see cref="TimeZoneDefinitionConfiguration"/> HAS
/// TWO. The derived Code satisfies the platform-wide master convention and is what a Masters
/// screen looks a row up by; the CultureCode is the BCP-47 string every OTHER table already
/// stores — <c>tenants.default_culture</c> and <c>users.preferred_language</c> both hold
/// "en-IN" today — and is therefore what a picker resolves against when an edit form opens on
/// an existing record. Both are looked up, so both are constrained.
///
/// ISO 639-1 IS NOT UNIQUE, deliberately. English (United Kingdom) and English (India) are two
/// catalogue rows that share the code "en", and constraining it would make the pair
/// unrepresentable — which is precisely the distinction the platform's existing "en-GB" and
/// "en-IN" values depend on.
/// </summary>
public sealed class LanguageConfiguration : IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> builder)
    {
        builder.ToTable("gm_languages");

        builder.HasKey(language => language.Id);

        builder.HasIndex(language => new { language.TenantKey, language.Code })
            .HasDatabaseName("ix_gm_languages_scope_code")
            .IsUnique();

        builder.HasIndex(language => new { language.TenantKey, language.CultureCode })
            .HasDatabaseName("ix_gm_languages_scope_culture")
            .IsUnique();

        // The picker's own ordering: active rows, recommended first, then by sort order.
        builder.HasIndex(language => new { language.Status, language.SortOrder })
            .HasDatabaseName("ix_gm_languages_status_order");

        builder.Property(language => language.Code).HasMaxLength(100).IsRequired();
        builder.Property(language => language.CultureCode).HasMaxLength(20).IsRequired();
        builder.Property(language => language.Iso2).HasMaxLength(2).IsRequired();
        builder.Property(language => language.Iso3).HasMaxLength(3);
        builder.Property(language => language.Name).HasMaxLength(150).IsRequired();
        builder.Property(language => language.NativeName).HasMaxLength(150);
        builder.Property(language => language.Notes).HasMaxLength(1000);

        builder.Property(language => language.Status).HasConversion<string>().HasMaxLength(40).IsRequired();

        builder.Property(language => language.Version).IsConcurrencyToken();

        // DisplayLabel is computed from Name and NativeName, so it is not a column.
        builder.Ignore(language => language.DisplayLabel);

        builder.ToTable(table =>
        {
            // BCP-47 as this platform uses it: "en" or "en-IN". Anything else — a bare country,
            // an underscore, a stray space — is the free-text era leaking back in.
            table.HasCheckConstraint(
                "ck_gm_languages_culture_format",
                "culture_code ~ '^[a-z]{2}(-[A-Za-z0-9]{2,8})?$'");
        });
    }
}

/// <summary>
/// The country-to-language link table.
///
/// Modelled on <see cref="CountryTimeZoneConfiguration"/> line for line, including the reason
/// the unique index is scoped on tenant_key rather than tenant_id: two NULLs are distinct
/// inside a PostgreSQL unique index, so a tenant_id-scoped constraint would not bite on the
/// platform rows at all — which are the only rows the seeder writes.
///
/// BOTH FOREIGN KEYS RESTRICT, so deleting a country cannot silently strip its language
/// mappings and deleting a language cannot do the same in reverse.
/// </summary>
public sealed class CountryLanguageConfiguration : IEntityTypeConfiguration<CountryLanguage>
{
    public void Configure(EntityTypeBuilder<CountryLanguage> builder)
    {
        builder.ToTable("gm_country_languages");

        builder.HasKey(link => link.Id);

        builder.HasIndex(link => new { link.TenantKey, link.CountryId, link.LanguageId })
            .HasDatabaseName("ix_gm_country_languages_scope_pair")
            .IsUnique();

        // The read path: "this country's languages, primary first". Covered end to end so a
        // cascading picker costs one index scan rather than a sort over the whole table.
        builder.HasIndex(link => new { link.CountryId, link.IsPrimary, link.SortOrder })
            .HasDatabaseName("ix_gm_country_languages_country_order");

        builder.HasIndex(link => link.LanguageId)
            .HasDatabaseName("ix_gm_country_languages_language");

        builder.Property(link => link.Version).IsConcurrencyToken();

        builder.HasOne(link => link.Country)
            .WithMany(country => country.CountryLanguages)
            .HasForeignKey(link => link.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(link => link.Language)
            .WithMany(language => language.CountryLanguages)
            .HasForeignKey(link => link.LanguageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
