// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Testing.Settings
{
    /// <summary>
    ///     Tenant settings served without a database: the scope's tenant with the content languages its
    ///     descriptor lists, the platform's first calendar, UTC and no stored entries, so a host under
    ///     test can complete its tenant scopes over an in-memory catalog.
    /// </summary>
    /// <remarks>
    ///     Every call is a hit under one fixed tag. A language the descriptor lists that the
    ///     distribution does not declare is skipped; with none left, the first offered language is the
    ///     primary. <see cref="FixedTenantSettingsOptions.Configure" /> adjusts the settings a test needs
    ///     in a particular shape.
    /// </remarks>
    /// <param name="accessor">The scope's request context, whose tenant the settings are for.</param>
    /// <param name="languages">The language catalogue.</param>
    /// <param name="calendars">The calendar registry.</param>
    /// <param name="options">How the settings are adjusted.</param>
    public sealed class FixedTenantSettingsCache(
        IRequestContextAccessor accessor,
        ILanguageCatalog languages,
        ICalendarRegistry calendars,
        FixedTenantSettingsOptions options) : ITenantSettingsCache
    {
        /// <summary>The tag every served instance carries.</summary>
        public static readonly VersionTag Tag = new(new Guid("5e771e65-0000-4000-8000-000000000001"));

        /// <summary>The settings built for this scope, once.</summary>
        private TenantSettings? _settings;

        /// <inheritdoc />
        public Task<CacheResult<TenantSettings>> GetAsync()
        {
            return Task.FromResult(new CacheResult<TenantSettings>(Settings(), Tag, CacheOutcome.Hit));
        }

        /// <inheritdoc />
        public Task<(byte[] Json, string WireTag)> GetForClientAsync()
        {
            // --- The client view: the server's, with its calendar codes and zone id, and no entries ---
            TenantSettings settings = Settings();
            TenantSettingsForClient client = new(
                settings.TenantId,
                settings.Names,
                settings.Languages,
                [.. settings.Calendars.Select(static calendar => calendar.Code)],
                settings.TimeZone.Id,
                settings.Region,
                settings.Digits,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal));
            return Task.FromResult((JsonSerializer.SerializeToUtf8Bytes(client, TellmaJson.Options), Tag.ToWire(TenantSettingsForClient.FormatVersion)));
        }

        /// <inheritdoc />
        public TenantSettings? Peek()
        {
            return Settings();
        }

        /// <summary>The scope's settings, built on first use.</summary>
        /// <returns>The settings.</returns>
        /// <exception cref="InvalidOperationException">The scope names no tenant.</exception>
        private TenantSettings Settings()
        {
            if (_settings is not null)
            {
                return _settings;
            }

            TenantDescriptor tenant = accessor.Current.Tenant
                ?? throw new InvalidOperationException("Tenant settings were read in a scope that names no tenant.");

            // --- The descriptor's languages the distribution declares, else its first offered one ---
            List<LanguageInfo> content = [.. tenant.Languages
                .Select(languages.Find)
                .OfType<LanguageInfo>()
                .DistinctBy(static language => language.Code, StringComparer.OrdinalIgnoreCase)
                .Take(3)];
            if (content.Count == 0)
            {
                content.Add(languages.Offered[0]);
            }

            TenantSettings settings = new()
            {
                TenantId = tenant.Id,
                Tag = Tag,
                Names = [tenant.Name, content.Count > 1 ? tenant.Name2 : null, content.Count > 2 ? tenant.Name3 : null],
                Languages = content,
                Shape = (MultilingualShape)content.Count,
                Calendars = [calendars[calendars.Codes[0]]],
                TimeZone = TimeZoneInfo.Utc,
                SqlServerTimeZoneName = "UTC",
                ModifiedAt = DateTimeOffset.UnixEpoch,
                Entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            };

            _settings = options.Configure?.Invoke(settings) ?? settings;
            return _settings;
        }
    }

    /// <summary>How <see cref="FixedTenantSettingsCache" /> adjusts the settings it serves.</summary>
    public sealed class FixedTenantSettingsOptions
    {
        /// <summary>Adjusts the built settings, such as a test's zone or region; null serves them as built.</summary>
        public Func<TenantSettings, TenantSettings>? Configure { get; init; }
    }
}
