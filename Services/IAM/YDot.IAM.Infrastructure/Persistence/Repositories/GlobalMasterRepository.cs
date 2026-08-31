using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Entities;

namespace YDot.IAM.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write side of the five global masters.
///
/// EVERY QUERY HERE GOES THROUGH THE GLOBAL FILTER, with no <c>IgnoreQueryFilters</c>
/// anywhere in the file. That is what makes the whole repository safe by construction: the
/// scoped filter on <see cref="GlobalMasterEntity"/> resolves to "platform rows OR mine", so
/// there is no method here - generic or otherwise - that can be talked into returning another
/// Organisation's row. It is also why none of these methods takes a TenantId to filter BY;
/// the only TenantId they accept names the scope a WRITE is targeting.
///
/// THE GENERIC METHODS RESOLVE THEIR SET WITH <c>Set&lt;TEntity&gt;()</c> rather than through
/// a switch on the type. EF has already built the model, so this is a dictionary lookup and
/// not reflection - and a sixth master added later needs no change to this class at all.
/// </summary>
public sealed class GlobalMasterRepository(IamDbContext context) : IGlobalMasterRepository
{
    // ---- The operations every master shares -----------------------------------------

    public Task<TEntity?> GetByIdAsync<TEntity>(Guid id, CancellationToken cancellationToken)
        where TEntity : GlobalMasterEntity =>
        context.Set<TEntity>().FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

    /// <summary>
    /// Whether a code is taken in the scope being written to.
    ///
    /// MATCHED ON <c>TenantKey</c>, NOT ON <c>TenantId</c>, and the difference is not
    /// cosmetic. A comparison of <c>TenantId == null</c> against a nullable column produces
    /// SQL <c>tenant_id = NULL</c>, which is never true - so the platform scope would report
    /// every code as free and the uniqueness check would pass for a duplicate. TenantKey
    /// mirrors null onto <c>Guid.Empty</c>, giving the platform rows a real value to compare
    /// against, which is the same reason the unique indexes are built on it.
    /// </summary>
    /// <summary>
    /// True when the code is already taken in the caller's own catalogue OR in the platform one.
    ///
    /// THE PLATFORM HALF OF THAT IS THE POINT, and it used to be missing. The check filtered on
    /// the caller's scope alone, so an Organisation could create its own "INR" beside the platform
    /// "INR" - both rows active, both returned by the same lookup, and a currency picker showing
    /// Indian Rupee twice. Amounts then referenced two different ids that display identically, and
    /// any total grouped by currency id silently split in two.
    ///
    /// A code has to mean one thing platform-wide, so a tenant may not shadow a platform code.
    /// When the caller IS the platform, scopeKey is already Guid.Empty and this reads exactly as
    /// it did before - the second clause collapses into the first.
    /// </summary>
    public Task<bool> CodeExistsAsync<TEntity>(
        string code, Guid? tenantId, Guid? excludeId, CancellationToken cancellationToken)
        where TEntity : GlobalMasterEntity
    {
        var scopeKey = tenantId ?? Guid.Empty;

        return context.Set<TEntity>()
            .Where(entity => entity.TenantKey == scopeKey || entity.TenantKey == Guid.Empty)
            .Where(entity => entity.Code == code)
            .Where(entity => excludeId == null || entity.Id != excludeId)
            .AnyAsync(cancellationToken);
    }

    public async Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken)
        where TEntity : GlobalMasterEntity =>
        await context.Set<TEntity>().AddAsync(entity, cancellationToken);

    public void Remove<TEntity>(TEntity entity) where TEntity : GlobalMasterEntity =>
        context.Set<TEntity>().Remove(entity);

    // ---- Countries -------------------------------------------------------------------------

    public Task<Country?> GetCountryAsync(Guid id, CancellationToken cancellationToken) =>
        context.Countries.FirstOrDefaultAsync(country => country.Id == id, cancellationToken);

    public Task<bool> Iso2ExistsAsync(
        string iso2, Guid? tenantId, Guid? excludeId, CancellationToken cancellationToken)
    {
        var scopeKey = tenantId ?? Guid.Empty;

        // Platform rows included for the same reason as CodeExistsAsync above: an ISO-2 code
        // identifies one country for everybody, so a tenant may not create a second "IN".
        return context.Countries
            .Where(country => country.TenantKey == scopeKey || country.TenantKey == Guid.Empty)
            .Where(country => country.Iso2 == iso2)
            .Where(country => excludeId == null || country.Id != excludeId)
            .AnyAsync(cancellationToken);
    }

    public Task<int> CountStatesForCountryAsync(Guid countryId, CancellationToken cancellationToken) =>
        context.StateProvinces.CountAsync(state => state.CountryId == countryId, cancellationToken);

    /// <summary>
    /// Counted straight off the denormalised <c>City.CountryId</c> rather than through the
    /// state table, which is the whole reason that column exists.
    /// </summary>
    public Task<int> CountCitiesForCountryAsync(Guid countryId, CancellationToken cancellationToken) =>
        context.Cities.CountAsync(city => city.CountryId == countryId, cancellationToken);

    // ---- States and provinces --------------------------------------------------------------------

    public Task<StateProvince?> GetStateProvinceAsync(Guid id, CancellationToken cancellationToken) =>
        context.StateProvinces.FirstOrDefaultAsync(state => state.Id == id, cancellationToken);

    public Task<int> CountCitiesForStateAsync(Guid stateProvinceId, CancellationToken cancellationToken) =>
        context.Cities.CountAsync(city => city.StateProvinceId == stateProvinceId, cancellationToken);

    // ---- Cities ----------------------------------------------------------------------------------------

    public Task<City?> GetCityAsync(Guid id, CancellationToken cancellationToken) =>
        context.Cities.FirstOrDefaultAsync(city => city.Id == id, cancellationToken);

    // ---- Currencies ------------------------------------------------------------------------------------------

    public Task<Currency?> GetCurrencyAsync(Guid id, CancellationToken cancellationToken) =>
        context.Currencies.FirstOrDefaultAsync(currency => currency.Id == id, cancellationToken);

    /// <summary>
    /// Countries naming this currency as their default.
    ///
    /// MATCHED ON THE CODE, because <c>Country.DefaultCurrencyCode</c> is a loose string
    /// reference rather than a foreign key - see the entity for why. That also means this
    /// count is the ONLY thing standing between a deleted currency and a set of countries
    /// pointing at nothing, since the database will not refuse the delete on its own.
    /// </summary>
    public Task<int> CountCountriesUsingCurrencyAsync(
        string currencyCode, CancellationToken cancellationToken) =>
        context.Countries.CountAsync(
            country => country.DefaultCurrencyCode == currencyCode, cancellationToken);

    // ---- Time zones ------------------------------------------------------------------------------------------

    public Task<TimeZoneDefinition?> GetTimeZoneAsync(Guid id, CancellationToken cancellationToken) =>
        context.TimeZones.FirstOrDefaultAsync(zone => zone.Id == id, cancellationToken);

    public Task<bool> IanaKeyExistsAsync(
        string ianaKey, Guid? tenantId, Guid? excludeId, CancellationToken cancellationToken)
    {
        var scopeKey = tenantId ?? Guid.Empty;

        // Platform rows included: an IANA key names one zone for everybody.
        return context.TimeZones
            .Where(zone => zone.TenantKey == scopeKey || zone.TenantKey == Guid.Empty)
            .Where(zone => zone.IanaKey == ianaKey)
            .Where(zone => excludeId == null || zone.Id != excludeId)
            .AnyAsync(cancellationToken);
    }

    public Task<int> CountStatesUsingTimeZoneAsync(Guid timeZoneId, CancellationToken cancellationToken) =>
        context.StateProvinces.CountAsync(
            state => state.DefaultTimeZoneId == timeZoneId, cancellationToken);
}
