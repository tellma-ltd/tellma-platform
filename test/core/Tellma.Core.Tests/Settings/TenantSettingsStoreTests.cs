// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
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
            using MetricCollector<long> invalid = new(host.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.InvalidEntries);
            TenantSettings settings = host.Build(Row(), (key, value)).Settings;

            Assert.Equal(1, invalid.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
            Assert.False(settings.Entries.ContainsKey(key));
            Assert.Equal(6, settings.Get(SampleKeys.Width));
            Assert.Equal(Cadence.Monthly, settings.Get(SampleKeys.Period));
        }

        [Fact]
        public void An_entry_no_feature_declares_is_dropped()
        {
            TenantSettings settings = host.Build(Row(), ("acme.retired", "true")).Settings;

            Assert.Empty(settings.Entries);
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
    }
}
