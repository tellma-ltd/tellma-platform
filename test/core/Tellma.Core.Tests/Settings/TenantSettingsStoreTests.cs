// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Queryex;
using Tellma.Core.Settings;
using static Tellma.Core.Tests.Settings.SettingsTestHost;

namespace Tellma.Core.Tests.Settings
{
    /// <summary>
    ///     How the settings store builds a tenant's settings from its row and entries: the routing
    ///     guard, the typed columns and their fallbacks, the entries a key reads, the secrets and the
    ///     client view.
    /// </summary>
    public sealed class TenantSettingsStoreTests : IAsyncDisposable
    {
        private readonly SettingsTestHost host = new();

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return host.DisposeAsync();
        }

        // --- The routing guard ---

        [Fact]
        public void A_row_of_another_tenant_fails_closed()
        {
            TenantDatabaseMismatchException failure = Assert.Throws<TenantDatabaseMismatchException>(() => host.Build(Row(tenantId: 8)));

            Assert.Equal(TenantId, failure.ExpectedTenantId);
            Assert.Equal(8, failure.ActualTenantId);
        }

        [Fact]
        public void The_placeholder_is_accepted_only_in_the_platforms_own_work_and_is_never_offered()
        {
            SettingsRows rows = new(Row(tenantId: 0), []);
            var tag = VersionTag.New();

            _ = Assert.Throws<TenantDatabaseMismatchException>(() => host.Store.Build(TenantId, allowPlaceholder: false, tag, rows));

            TenantSettingsEntry entry = host.Store.Build(TenantId, allowPlaceholder: true, tag, rows);
            Assert.True(entry.IsPlaceholder);
            Assert.Equal(TenantId, entry.Settings.TenantId);

            // An offer of the placeholder stores nothing: the next request reads the row again.
            host.Store.Offer(TenantId, entry, tag);
            Assert.False(host.Store.IsCurrent(TenantId, tag));
        }

        [Fact]
        public void A_database_without_its_row_fails()
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => host.Store.Build(TenantId, false, VersionTag.New(), new SettingsRows(null, [])));

            Assert.Contains("no row of core.Settings", failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_offered_entry_is_current_under_its_tag_alone()
        {
            var tag = VersionTag.New();
            TenantSettingsEntry entry = host.Store.Build(TenantId, false, tag, new SettingsRows(Row(), []));

            host.Store.Offer(TenantId, entry, tag);

            Assert.True(host.Store.IsCurrent(TenantId, tag));
            Assert.False(host.Store.IsCurrent(TenantId, VersionTag.New()));
            Assert.False(host.Store.IsCurrent(TenantId + 1, tag));
        }

        // --- The typed columns ---

        [Fact]
        public void A_sound_row_reads_as_typed_settings()
        {
            TenantSettings settings = host.Build(Row() with
            {
                SecondaryLanguage = "ar",
                SecondaryCalendar = "uq",
                TimeZone = "Asia/Riyadh",
                Region = "SA",
                Digits = nameof(DigitStyle.Native),
            }).Settings;

            Assert.Equal(TenantId, settings.TenantId);
            Assert.Equal(["Acme", "أكمي", null], settings.Names);
            Assert.Equal(["en", "ar"], settings.Languages.Select(static language => language.Code));
            Assert.Equal(MultilingualShape.PrimaryAndSecondary, settings.Shape);
            Assert.Equal(["gc", "uq"], settings.Calendars.Select(static calendar => calendar.Code));
            Assert.Equal("Asia/Riyadh", settings.TimeZone.Id);
            Assert.Equal("Arab Standard Time", settings.SqlServerTimeZoneName);
            Assert.Equal("SA", settings.Region);
            Assert.Equal(DigitStyle.Native, settings.Digits);
            Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), settings.ModifiedAt);
        }

        [Fact]
        public void Values_the_composition_cannot_resolve_fall_back_to_the_platforms_defaults()
        {
            using MetricCollector<long> invalid = Invalid();
            TenantSettings settings = host.Build(Row() with
            {
                PrimaryLanguage = "zz",
                SecondaryLanguage = "ar",
                PrimaryCalendar = "xx",
                SecondaryCalendar = "yy",
                TimeZone = "Mars/Olympus",
                Digits = "1",
            }).Settings;

            // An unresolvable primary language falls back to English; the secondary still reads.
            Assert.Equal(["en", "ar"], settings.Languages.Select(static language => language.Code));
            Assert.Equal(["gc"], settings.Calendars.Select(static calendar => calendar.Code));
            Assert.Same(TimeZoneInfo.Utc, settings.TimeZone);
            Assert.Equal("UTC", settings.SqlServerTimeZoneName);
            Assert.Null(settings.Digits);

            // Each fallback is logged critical and counted: the language, both calendars, the zone and the digits.
            Assert.Equal(
                [LogLevel.Critical, LogLevel.Critical, LogLevel.Critical, LogLevel.Critical, LogLevel.Critical],
                Records("SettingsValueUnresolvable").Select(static record => record.Level));
            Assert.Equal(5, invalid.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
        }

        [Fact]
        public void A_zone_the_pinned_table_does_not_map_falls_back_to_utc_whatever_the_host_resolves()
        {
            // A fixed-offset zone the host's time-zone data may well have, but which no tenant can
            // choose and the query engine's table does not map to a Windows name.
            TenantSettings settings = host.Build(Row() with { TimeZone = "Etc/GMT+3" }).Settings;

            Assert.Same(TimeZoneInfo.Utc, settings.TimeZone);
            Assert.Equal("UTC", settings.SqlServerTimeZoneName);
            _ = Assert.Single(Records("SettingsValueUnresolvable"));
        }

        [Fact]
        public void The_zones_windows_name_is_the_pinned_tables()
        {
            Assert.True(QueryexTimeZones.TryGetWindowsName("Africa/Addis_Ababa", out string? expected));

            Assert.Equal(expected, host.Build(Row() with { TimeZone = "Africa/Addis_Ababa" }).Settings.SqlServerTimeZoneName);
        }

        [Fact]
        public async Task The_placeholder_reads_as_the_platforms_defaults_without_an_event_where_english_is_not_offered()
        {
            await using SettingsTestHost arabic = new(languages: ["ar"]);
            using MetricCollector<long> invalid = new(arabic.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.InvalidEntries);

            TenantSettings settings = arabic.Store.Build(TenantId, allowPlaceholder: true, VersionTag.New(), new SettingsRows(Row(tenantId: 0), [])).Settings;

            Assert.Equal(["ar"], settings.Languages.Select(static language => language.Code));
            Assert.Equal(["gc"], settings.Calendars.Select(static calendar => calendar.Code));
            Assert.Same(TimeZoneInfo.Utc, settings.TimeZone);
            Assert.Empty(arabic.Log.Collector.GetSnapshot());
            Assert.Equal(0, invalid.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));

            // A provisioned row naming the same language is the tenant's own, and its fallback is reported.
            _ = arabic.Build(Row());
            _ = Assert.Single(arabic.Log.Collector.GetSnapshot(), static record => record.Id.Name == "SettingsValueUnresolvable");
        }

        [Fact]
        public void An_unresolvable_or_repeated_secondary_language_ends_the_list()
        {
            Assert.Equal(["en"], host.Build(Row() with { SecondaryLanguage = "zz", TertiaryLanguage = "ar" }).Settings.Languages.Select(static language => language.Code));
            Assert.Equal(["en"], host.Build(Row() with { SecondaryLanguage = "en", TertiaryLanguage = "ar" }).Settings.Languages.Select(static language => language.Code));
            Assert.Equal(MultilingualShape.Primary, host.Build(Row() with { SecondaryLanguage = "zz" }).Settings.Shape);
        }

        // --- The entries ---

        [Fact]
        public void A_stored_entry_reads_as_its_key_and_an_unset_key_as_its_default()
        {
            TenantSettings settings = host.Build(Row(), ("acme.posting.width", "9"), ("acme.period", "\"Quarterly\"")).Settings;

            Assert.Equal(9, settings.Get(SampleKeys.Width));
            Assert.Equal(Cadence.Quarterly, settings.Get(SampleKeys.Period));
            Assert.True(settings.Get(SampleKeys.AutoNumber));
            Assert.Equal([1, 2], settings.Get(SampleKeys.Codes));
        }

        [Theory]
        [InlineData("acme.posting.width", "not json")]
        [InlineData("acme.posting.width", "\"nine\"")]
        [InlineData("acme.posting.width", "40")]
        [InlineData("acme.period", "\"Weekly\"")]
        [InlineData("acme.period", "1")]
        public void An_entry_that_does_not_read_as_its_key_is_dropped(string key, string value)
        {
            using MetricCollector<long> invalid = Invalid();
            TenantSettings settings = host.Build(Row(), (key, value)).Settings;

            Assert.Equal(1, invalid.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
            Assert.Equal(LogLevel.Warning, Assert.Single(Records("SettingsEntryInvalid")).Level);
            Assert.False(settings.Entries.ContainsKey(key));
            Assert.Equal(6, settings.Get(SampleKeys.Width));
            Assert.Equal(Cadence.Monthly, settings.Get(SampleKeys.Period));
        }

        [Fact]
        public void An_entry_no_feature_declares_is_dropped()
        {
            using MetricCollector<long> invalid = Invalid();
            TenantSettings settings = host.Build(Row(), ("acme.retired", "true")).Settings;

            Assert.Empty(settings.Entries);
            Assert.Equal(LogLevel.Warning, Assert.Single(Records("SettingsEntryUndeclared")).Level);
            Assert.Equal(0, invalid.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
        }

        // --- Secrets ---

        [Fact]
        public void A_secret_is_kept_protected_and_unprotected_when_read()
        {
            string stored = host.Secret("\"s3cr3t\"");
            TenantSettings settings = host.Build(Row(), ("acme.erp.apiKey", stored)).Settings;

            Assert.Equal(JsonValueKind.String, settings.Entries["acme.erp.apiKey"].ValueKind);
            Assert.DoesNotContain("s3cr3t\"", settings.Entries["acme.erp.apiKey"].GetRawText(), StringComparison.Ordinal);
            Assert.Equal("s3cr3t", settings.Get(SampleKeys.ApiKey));
        }

        [Fact]
        public void A_secret_protected_for_another_tenant_or_not_protected_reads_as_its_default_and_is_reported()
        {
            using MetricCollector<long> invalid = new(host.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.InvalidEntries);
            TenantSettings other = host.Build(Row(), ("acme.erp.apiKey", host.Secret("\"s3cr3t\"", tenantId: 8))).Settings;
            TenantSettings plain = host.Build(Row(), ("acme.erp.apiKey", "\"s3cr3t\"")).Settings;

            Assert.Null(other.Get(SampleKeys.ApiKey));
            Assert.Null(plain.Get(SampleKeys.ApiKey));
            Assert.Equal(2, invalid.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
        }

        [Fact]
        public void A_secret_is_protected_under_the_settings_purpose_and_the_tenant_id_and_no_other()
        {
            // Hand-built protectors: the purpose is a durable contract of every stored secret.
            string Stored(params string[] purposes)
            {
                return JsonSerializer.Serialize(host.Protection.CreateProtector(purposes[0], purposes[1..]).Protect("\"s3cr3t\""));
            }

            Assert.Equal("s3cr3t", host.Build(Row(), ("acme.erp.apiKey", Stored("Tellma.Core.Settings.Secret", "7"))).Settings.Get(SampleKeys.ApiKey));

            TenantSettings other = host.Build(Row(), ("acme.erp.apiKey", Stored("Tellma.Other", "7"))).Settings;
            Assert.Null(other.Get(SampleKeys.ApiKey));
            Assert.Equal(LogLevel.Critical, Assert.Single(Records("SettingsSecretUnreadable")).Level);
        }

        [Fact]
        public async Task On_a_host_without_data_protection_a_secret_reads_as_its_default_and_is_reported_as_critical()
        {
            await using SettingsTestHost unprotected = new(withProtection: false);
            using MetricCollector<long> invalid = new(unprotected.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.InvalidEntries);
            TenantSettings settings = unprotected.Build(Row(), ("acme.erp.apiKey", unprotected.Secret("\"s3cr3t\""))).Settings;

            Assert.Null(settings.Get(SampleKeys.ApiKey));
            FakeLogRecord record = Assert.Single(unprotected.Log.Collector.GetSnapshot(), static record => record.Id.Name == "SettingsSecretUnreadable");
            Assert.Equal(LogLevel.Critical, record.Level);
            Assert.Equal(1, invalid.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
        }

        // --- The client view ---

        [Fact]
        public void The_client_view_carries_every_client_key_stored_or_default_and_nothing_else()
        {
            TenantSettingsEntry entry = host.Build(
                Row() with { SecondaryLanguage = "ar", SecondaryCalendar = "uq", Region = "SA", Digits = nameof(DigitStyle.Latin) },
                ("acme.posting.autoNumber", "false"),
                ("acme.posting.width", "9"),
                ("acme.erp.apiKey", host.Secret("\"s3cr3t\"")));

            using var document = JsonDocument.Parse(entry.ClientJson);
            JsonElement root = document.RootElement;
            Assert.Equal(TenantId, root.GetProperty("tenantId").GetInt32());
            Assert.Equal(["gc", "uq"], root.GetProperty("calendars").EnumerateArray().Select(static code => code.GetString()));
            Assert.Equal("UTC", root.GetProperty("timeZone").GetString());
            Assert.Equal("SA", root.GetProperty("region").GetString());
            Assert.Equal("Latin", root.GetProperty("digits").GetString());
            Assert.Equal(["en", "ar"], root.GetProperty("languages").EnumerateArray().Select(static language => language.GetProperty("code").GetString()));

            // The client keys alone, the stored one as stored and the others as their defaults.
            JsonElement entries = root.GetProperty("entries");
            Assert.Equal(["acme.posting.autoNumber", "acme.period", "acme.codes"], entries.EnumerateObject().Select(static property => property.Name));
            Assert.False(entries.GetProperty("acme.posting.autoNumber").GetBoolean());
            Assert.Equal("Monthly", entries.GetProperty("acme.period").GetString());
            Assert.Equal("[1,2]", entries.GetProperty("acme.codes").GetRawText());
            Assert.DoesNotContain("s3cr3t", System.Text.Encoding.UTF8.GetString(entry.ClientJson), StringComparison.Ordinal);
        }

        [Fact]
        public void The_unset_region_and_digits_are_left_out_of_the_client_view()
        {
            using var document = JsonDocument.Parse(host.Build(Row()).ClientJson);

            Assert.False(document.RootElement.TryGetProperty("region", out _));
            Assert.False(document.RootElement.TryGetProperty("digits", out _));
        }

        /// <summary>Collects the store's count of entries read as their defaults.</summary>
        /// <returns>The collector.</returns>
        private MetricCollector<long> Invalid()
        {
            return new(host.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.InvalidEntries);
        }

        /// <summary>The store's log records of one event.</summary>
        /// <param name="name">The event's name.</param>
        /// <returns>The records, in order.</returns>
        private List<FakeLogRecord> Records(string name)
        {
            return [.. host.Log.Collector.GetSnapshot().Where(record => record.Id.Name == name)];
        }
    }
}
