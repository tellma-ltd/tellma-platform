// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Caching;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Settings
{
    // The store is spec 0013 §5.6's settings kind of the versioned cache: one entry per tenant, the
    // server's view beside the client's serialized one, loaded by §5.7's cold load.
    /// <summary>
    ///     The process's tenant settings: one entry per tenant, validated by the tenant's
    ///     <c>settings</c> tag, holding the server's view and the client's view serialized once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A miss loads through one read batch on the caller's handle, which the cold-load
    ///         contributor completes with the two statements of the cold load, after its prelude. The
    ///         loader asserts the database's tenant, resolves what the typed row names against the
    ///         composition (falling back to the platform defaults with a critical event when it
    ///         cannot), drops undeclared entries and those that do not read as their key, and keeps a
    ///         secret entry as stored.
    ///     </para>
    ///     <para>
    ///         A secret is unprotected only when it is first read, through the tenant's protector,
    ///         so neither the migrator nor a host without the key ring touches it. A host without data
    ///         protection reads every secret as its default.
    ///     </para>
    /// </remarks>
    internal sealed class TenantSettingsStore : VersionedCache<TenantCacheKey, TenantSettingsEntry>
    {
        /// <summary>The platform's default language, which stands in for one the composition cannot resolve.</summary>
        public const string DefaultLanguage = "en";

        /// <summary>The platform's default zone, which stands in for one the host cannot resolve.</summary>
        public const string DefaultTimeZone = "UTC";

        /// <summary>The load each of the store's own batches expects its contributor to complete.</summary>
        private readonly ConditionalWeakTable<DataBatch, SettingsLoadRequest> _requests = [];

        /// <summary>The setting keys, which parse the stored entries.</summary>
        private readonly ISettingKeyRegistry _keys;

        /// <summary>The language catalogue, which resolves the content languages.</summary>
        private readonly ILanguageCatalog _languages;

        /// <summary>The calendar registry, which resolves the calendars.</summary>
        private readonly ICalendarRegistry _calendars;

        /// <summary>The host's data protection; null in a host without it.</summary>
        private readonly IDataProtectionProvider? _protection;

        /// <summary>The logger.</summary>
        private readonly ILogger _logger;

        /// <summary>The count of stored settings read as their defaults.</summary>
        private readonly Counter<long> _invalid;

        /// <summary>Creates the store.</summary>
        /// <param name="keys">The setting keys.</param>
        /// <param name="languages">The language catalogue.</param>
        /// <param name="calendars">The calendar registry.</param>
        /// <param name="options">The cache limits.</param>
        /// <param name="meterFactory">The meter factory.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="protection">The host's data protection; null in a host without it, which reads every secret as its default.</param>
        /// <exception cref="ArgumentNullException">An argument other than <paramref name="protection" /> is null.</exception>
        public TenantSettingsStore(
            ISettingKeyRegistry keys,
            ILanguageCatalog languages,
            ICalendarRegistry calendars,
            IOptions<CacheOptions> options,
            IMeterFactory meterFactory,
            ILogger<TenantSettingsStore> logger,
            IDataProtectionProvider? protection = null)
            : base(CacheKinds.Settings, (options ?? throw new ArgumentNullException(nameof(options))).Value.SettingsEntries, meterFactory)
        {
            ArgumentNullException.ThrowIfNull(keys);
            ArgumentNullException.ThrowIfNull(languages);
            ArgumentNullException.ThrowIfNull(calendars);
            ArgumentNullException.ThrowIfNull(logger);
            _keys = keys;
            _languages = languages;
            _calendars = calendars;
            _protection = protection;
            _logger = logger;
            _invalid = meterFactory.Create(CacheTelemetryNames.MeterName).CreateCounter<long>(
                CacheTelemetryNames.InvalidEntries,
                "{entry}",
                "Stored settings read as their keys' defaults because they were invalid or could not be unprotected.");
        }

        /// <summary>Whether the store holds a tenant's settings current for a tag, without counting a cache request.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="currentTag">The tag the snapshot holds.</param>
        /// <returns>True when the entry is present and current.</returns>
        public bool IsCurrent(int tenantId, VersionTag currentTag)
        {
            ArgumentNullException.ThrowIfNull(currentTag);
            return Lookup(new TenantCacheKey(tenantId), currentTag, out _) == CacheOutcome.Hit;
        }

        /// <summary>
        ///     Takes the load a batch of the store's own expects its contributor to complete: one per
        ///     batch, handed out once.
        /// </summary>
        /// <param name="batch">The batch being composed.</param>
        /// <param name="request">The load, when the batch is one of the store's.</param>
        /// <returns>True when the batch is one of the store's.</returns>
        public bool TryTakeRequest(DataBatch batch, out SettingsLoadRequest request)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (_requests.TryGetValue(batch, out SettingsLoadRequest? found))
            {
                _ = _requests.Remove(batch);
                request = found;
                return true;
            }

            request = null!;
            return false;
        }

        /// <summary>
        ///     Builds a tenant's entry from the rows the cold load read: the tenant asserted, the typed
        ///     row resolved against the composition, the entries parsed, both views made.
        /// </summary>
        /// <param name="tenantId">The routed tenant.</param>
        /// <param name="allowPlaceholder">Whether the unprovisioned placeholder is accepted, as it is in the platform's own work alone.</param>
        /// <param name="tag">The tag the batch's prelude returned for the settings.</param>
        /// <param name="rows">What the cold load read.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="TenantDatabaseMismatchException">The database holds another tenant's settings, or the placeholder where it is not accepted.</exception>
        /// <exception cref="InvalidOperationException">The database holds no typed row.</exception>
        public TenantSettingsEntry Build(int tenantId, bool allowPlaceholder, VersionTag tag, SettingsRows rows)
        {
            ArgumentNullException.ThrowIfNull(tag);
            ArgumentNullException.ThrowIfNull(rows);
            SettingsRow row = rows.Row ?? throw new InvalidOperationException(
                "The tenant database holds no row of core.Settings. Its migrations seed the row; run the migrator against the database.");

            // --- The routing guard: the database is the routed tenant's, or the placeholder in the platform's own work ---
            bool isPlaceholder = row.TenantId == Abstractions.Settings.Settings.PlaceholderTenantId;
            if (row.TenantId != tenantId && !(isPlaceholder && allowPlaceholder))
            {
                throw Mismatch(tenantId, row.TenantId);
            }

            // --- The typed row, resolved; what cannot be resolved falls back to the platform's defaults ---
            // The placeholder's values are the platform's own defaults, which the next provisioning
            // step replaces: it reads as them directly, with no event, even in a distribution that
            // does not offer English.
            List<LanguageInfo> languages = isPlaceholder ? [DefaultLanguageInfo()] : ResolveLanguages(row, tenantId);
            List<ICalendarSystem> calendars = isPlaceholder ? [GregorianCalendar()] : ResolveCalendars(row, tenantId);
            (TimeZoneInfo zone, string sqlZone) = isPlaceholder ? (TimeZoneInfo.Utc, DefaultTimeZone) : ResolveZone(row.TimeZone, tenantId);
            DigitStyle? digits = isPlaceholder ? null : ResolveDigits(row.Digits, tenantId);

            // --- The entries: undeclared ones dropped, others parsed against their keys, secrets kept as stored ---
            Dictionary<string, JsonElement> entries = ReadEntries(rows.Entries, tenantId);

            // --- The server's view; its secrets unprotect through the tenant's protector on first read ---
            IDataProtector? protector = _protection is null ? null : SettingSecrets.For(_protection, tenantId);
            TenantSettings settings = new()
            {
                TenantId = tenantId,
                Tag = tag,
                Names = [row.Name, row.Name2, row.Name3],
                Languages = languages,
                Shape = (MultilingualShape)languages.Count,
                Calendars = calendars,
                TimeZone = zone,
                SqlServerTimeZoneName = sqlZone,
                Region = row.Region,
                Digits = digits,
                ModifiedAt = row.ModifiedAt,
                Entries = entries.ToFrozenDictionary(StringComparer.Ordinal),
                Unprotect = protector is null ? null : text => protector.Unprotect(text),
                ReportProblem = (key, problem) => Report(key, problem, tenantId),
            };

            return new TenantSettingsEntry(settings, SerializeForClient(settings), isPlaceholder);
        }

        /// <summary>Logs a routing mismatch, critical, and creates the exception that fails the work closed.</summary>
        /// <param name="expected">The routed tenant.</param>
        /// <param name="actual">The tenant the database's settings name; zero for the placeholder.</param>
        /// <returns>The exception to throw.</returns>
        public TenantDatabaseMismatchException Mismatch(int expected, int actual)
        {
            SettingsLog.TenantDatabaseMismatch(_logger, expected, actual);
            return new TenantDatabaseMismatchException(expected, actual);
        }

        /// <summary>
        ///     Stores an entry a batch read outside the store's own load, best effort: a placeholder, or
        ///     an entry under no tag, is never stored, and a store disposed at host stop skips it.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="entry">The entry.</param>
        /// <param name="tag">The tag the batch's prelude returned.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public void Offer(int tenantId, TenantSettingsEntry entry, VersionTag tag)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(tag);
            if (entry.IsPlaceholder || tag == VersionTag.None)
            {
                return;
            }

            try
            {
                Set(new TenantCacheKey(tenantId), entry, tag);
            }
            catch (ObjectDisposedException)
            {
                SettingsLog.SettingsNotStored(_logger, tenantId);
            }
        }

        /// <inheritdoc />
        protected override async Task<CacheLoad<TenantSettingsEntry>> LoadAsync(TenantCacheKey key, ITenantDatabase database, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(database);

            // --- One read batch on the caller's handle; its contributor appends the cold load after the prelude ---
            if (database.CreateBatch(BatchPurpose.Read) is not DataBatch batch)
            {
                throw new InvalidOperationException("The tenant's settings load on the platform's own tenant database handle.");
            }

            SettingsLoadRequest request = new();
            _requests.AddOrUpdate(batch, request);
            BatchOutcome outcome = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            // A handle of another tenant would store that tenant's settings under this key.
            if (outcome.VersionTags.TenantId != key.TenantId)
            {
                throw new InvalidOperationException(
                    $"The settings of tenant {key.TenantId} were loaded on a handle of tenant {outcome.VersionTags.TenantId}; load a tenant's settings on a handle of the same tenant.");
            }

            if (!request.Result.IsCompleted)
            {
                throw new InvalidOperationException(
                    "The settings load batch returned no settings: the settings cold-load contributor is not composed in the scope, or the batch carried the connect prologue without absorbing the load.");
            }

            // The placeholder is served to the platform's own work, never stored for anyone else's.
            (TenantSettingsEntry entry, VersionTag tag) = request.Result.Value;
            return CacheLoad.Loaded(entry, entry.IsPlaceholder ? VersionTag.None : tag);
        }

        /// <summary>The content languages in position order; the primary falls back to the default, a later one is dropped with what follows it.</summary>
        /// <param name="row">The typed row.</param>
        /// <param name="tenantId">The tenant, for the events.</param>
        /// <returns>One to three languages.</returns>
        private List<LanguageInfo> ResolveLanguages(SettingsRow row, int tenantId)
        {
            List<LanguageInfo> languages = [ResolveLanguage(nameof(SettingsRow.PrimaryLanguage), row.PrimaryLanguage, tenantId) ?? DefaultLanguageInfo()];
            foreach ((string column, string? code) in new[] { (nameof(SettingsRow.SecondaryLanguage), row.SecondaryLanguage), (nameof(SettingsRow.TertiaryLanguage), row.TertiaryLanguage) })
            {
                // A tertiary language without a secondary one names no position: the constraints forbid it.
                if (code is null)
                {
                    break;
                }

                // A language that does not resolve, or repeats an earlier one after a fallback, ends
                // the list, so each later name keeps its position.
                LanguageInfo? language = ResolveLanguage(column, code, tenantId);
                if (language is null || languages.Exists(existing => string.Equals(existing.Code, language.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }

                languages.Add(language);
            }

            return languages;
        }

        /// <summary>A content language the distribution offers.</summary>
        /// <param name="column">The column, for the event.</param>
        /// <param name="code">The stored code.</param>
        /// <param name="tenantId">The tenant, for the event.</param>
        /// <returns>The language, or null when the distribution does not offer it.</returns>
        private LanguageInfo? ResolveLanguage(string column, string code, int tenantId)
        {
            if (_languages.IsOffered(code) && _languages.Find(code) is LanguageInfo language)
            {
                return language;
            }

            Unresolvable(column, code, tenantId);
            return null;
        }

        /// <summary>The platform's default language when the distribution offers it, else the distribution's first.</summary>
        /// <returns>The language.</returns>
        private LanguageInfo DefaultLanguageInfo()
        {
            return _languages.IsOffered(DefaultLanguage) && _languages.Find(DefaultLanguage) is LanguageInfo english ? english : _languages.Offered[0];
        }

        /// <summary>The calendars, primary first; the primary falls back to Gregorian, the secondary is dropped.</summary>
        /// <param name="row">The typed row.</param>
        /// <param name="tenantId">The tenant, for the events.</param>
        /// <returns>One or two calendars.</returns>
        private List<ICalendarSystem> ResolveCalendars(SettingsRow row, int tenantId)
        {
            ICalendarSystem primary = ResolveCalendar(nameof(SettingsRow.PrimaryCalendar), row.PrimaryCalendar, tenantId) ?? GregorianCalendar();
            List<ICalendarSystem> calendars = [primary];
            if (row.SecondaryCalendar is string code
                && ResolveCalendar(nameof(SettingsRow.SecondaryCalendar), code, tenantId) is ICalendarSystem secondary
                && !string.Equals(secondary.Code, primary.Code, StringComparison.Ordinal))
            {
                calendars.Add(secondary);
            }

            return calendars;
        }

        /// <summary>The Gregorian calendar, which the platform always composes.</summary>
        /// <returns>The calendar.</returns>
        /// <exception cref="InvalidOperationException">The registry holds none.</exception>
        private ICalendarSystem GregorianCalendar()
        {
            return _calendars.TryGet(CalendarCodes.Gregorian)
                ?? throw new InvalidOperationException("The calendar registry holds no Gregorian calendar, which the platform always composes.");
        }

        /// <summary>A calendar of the registry.</summary>
        /// <param name="column">The column, for the event.</param>
        /// <param name="code">The stored code.</param>
        /// <param name="tenantId">The tenant, for the event.</param>
        /// <returns>The calendar, or null when the registry has none with the code.</returns>
        private ICalendarSystem? ResolveCalendar(string column, string code, int tenantId)
        {
            if (_calendars.TryGet(code) is ICalendarSystem calendar)
            {
                return calendar;
            }

            Unresolvable(column, code, tenantId);
            return null;
        }

        /// <summary>
        ///     Resolves a tenant's zone as the settings require it: the host's time-zone data has the
        ///     zone, and the query engine's pinned table maps it to the Windows name SQL Server's zone
        ///     conversions take.
        /// </summary>
        /// <param name="id">The IANA id.</param>
        /// <param name="zone">The host's zone when the method returns true; otherwise null.</param>
        /// <param name="sqlServerName">The Windows zone name when the method returns true; otherwise null.</param>
        /// <returns>True when both resolve.</returns>
        internal static bool TryResolveZone(string id, [NotNullWhen(true)] out TimeZoneInfo? zone, [NotNullWhen(true)] out string? sqlServerName)
        {
            // The Windows name is the pinned table's, never the host ICU's, so the SQL zone of the
            // tenant's day and a query's local(..., '<id>') always name the same zone.
            zone = null;
            return QueryexTimeZones.TryGetWindowsName(id, out sqlServerName)
                && TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone);
        }

        /// <summary>The tenant's zone and its Windows id, which SQL Server's zone conversions take; UTC when either does not resolve.</summary>
        /// <param name="id">The stored IANA id.</param>
        /// <param name="tenantId">The tenant, for the event.</param>
        /// <returns>The zone and its Windows id.</returns>
        private (TimeZoneInfo Zone, string SqlServerName) ResolveZone(string id, int tenantId)
        {
            if (TryResolveZone(id, out TimeZoneInfo? zone, out string? sqlServerName))
            {
                return (zone, sqlServerName);
            }

            Unresolvable(nameof(SettingsRow.TimeZone), id, tenantId);
            return (TimeZoneInfo.Utc, DefaultTimeZone);
        }

        /// <summary>The tenant's default digits; none when the stored value names no digit style.</summary>
        /// <param name="digits">The stored value.</param>
        /// <param name="tenantId">The tenant, for the event.</param>
        /// <returns>The digits, or null.</returns>
        private DigitStyle? ResolveDigits(string? digits, int tenantId)
        {
            if (digits is null)
            {
                return null;
            }

            // By name only: a number that happens to parse as a member is not a stored style.
            if (Enum.GetNames<DigitStyle>().Contains(digits, StringComparer.Ordinal))
            {
                return Enum.Parse<DigitStyle>(digits);
            }

            Unresolvable(nameof(SettingsRow.Digits), digits, tenantId);
            return null;
        }

        /// <summary>The stored entries a declared key reads: a secret as stored, every other parsed and checked.</summary>
        /// <param name="stored">The stored entries.</param>
        /// <param name="tenantId">The tenant, for the events.</param>
        /// <returns>The entries kept, by key.</returns>
        private Dictionary<string, JsonElement> ReadEntries(IReadOnlyList<KeyValuePair<string, string>> stored, int tenantId)
        {
            Dictionary<string, JsonElement> entries = new(StringComparer.Ordinal);
            foreach ((string name, string value) in stored)
            {
                // --- A key no feature declares: ignored ---
                if (_keys.Find(name) is not SettingKey key)
                {
                    SettingsLog.SettingsEntryUndeclared(_logger, name, tenantId);
                    continue;
                }

                // --- One JSON value, whatever the key ---
                JsonElement json;
                try
                {
                    using var document = JsonDocument.Parse(value);
                    json = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    Report(name, SettingEntryProblem.Invalid, tenantId);
                    continue;
                }

                // --- A secret is kept as stored, protected; every other value must read as its key and pass its check ---
                if (key.Visibility != SettingVisibility.Secret && !key.TryReadValue(json, out _, out _))
                {
                    Report(name, SettingEntryProblem.Invalid, tenantId);
                    continue;
                }

                entries[name] = json;
            }

            return entries;
        }

        /// <summary>The client's view as UTF-8 JSON: every client-visible key, stored or default, and nothing else.</summary>
        /// <param name="settings">The server's view.</param>
        /// <returns>The bytes.</returns>
        private byte[] SerializeForClient(TenantSettings settings)
        {
            Dictionary<string, JsonElement> visible = new(StringComparer.Ordinal);
            foreach (SettingKey key in _keys.Keys.Where(static key => key.Visibility == SettingVisibility.Client))
            {
                visible[key.Name] = settings.Entries.TryGetValue(key.Name, out JsonElement stored) ? stored : key.DefaultJson;
            }

            TenantSettingsForClient client = new(
                settings.TenantId,
                settings.Names,
                settings.Languages,
                [.. settings.Calendars.Select(static calendar => calendar.Code)],
                settings.TimeZone.Id,
                settings.Region,
                settings.Digits,
                visible);
            return JsonSerializer.SerializeToUtf8Bytes(client, TellmaJson.Options);
        }

        /// <summary>Logs and counts a stored setting read as its default.</summary>
        /// <param name="key">The key.</param>
        /// <param name="problem">Why.</param>
        /// <param name="tenantId">The tenant.</param>
        private void Report(string key, SettingEntryProblem problem, int tenantId)
        {
            if (problem == SettingEntryProblem.SecretUnreadable)
            {
                SettingsLog.SettingsSecretUnreadable(_logger, key, tenantId);
            }
            else
            {
                SettingsLog.SettingsEntryInvalid(_logger, key, tenantId);
            }

            _invalid.Add(1);
        }

        /// <summary>Logs and counts a typed setting the composition cannot resolve.</summary>
        /// <param name="column">The column.</param>
        /// <param name="value">The stored value.</param>
        /// <param name="tenantId">The tenant.</param>
        private void Unresolvable(string column, string value, int tenantId)
        {
            SettingsLog.SettingsValueUnresolvable(_logger, column, value, tenantId);
            _invalid.Add(1);
        }
    }

    /// <summary>The load a batch of the settings store expects its contributor to complete.</summary>
    internal sealed class SettingsLoadRequest
    {
        /// <summary>The entry read and the tag it is current for, completed when the batch's results complete.</summary>
        public BatchResult<(TenantSettingsEntry Entry, VersionTag Tag)> Result { get; } = new();
    }
}
