// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Time.Testing;
using System.Security.Cryptography;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Abstractions.Tests.Settings
{
    /// <summary>
    ///     Reading a tenant's settings: stored values, defaults, values that do not read or that a key
    ///     refuses, secrets decrypted on first read, each read once and remembered, problems reported
    ///     once; the tenant's date on its zone's wall clock; equality and copies.
    /// </summary>
    public class TenantSettingsTests
    {
        private static readonly SettingKey<bool> AutoNumber = new("gl.posting.autoNumberOnPost", true);
        private static readonly SettingKey<int> Width = new("gl.posting.width", 6, validate: static w => w is >= 1 and <= 12 ? null : "Gl.WidthOutOfRange");
        private static readonly SettingKey<long> Threshold = new("gl.posting.threshold", 0L);
        private static readonly SettingKey<string?> ApiKey = new("acme.erp.apiKey", null, visibility: SettingVisibility.Secret);

        [Fact]
        public void A_stored_value_reads_and_an_absent_one_reads_as_the_default()
        {
            TenantSettings settings = Settings(new() { [AutoNumber.Name] = Parse("false"), [Threshold.Name] = Parse("\"9007199254740993\"") });

            Assert.False(settings.Get(AutoNumber));
            Assert.Equal(9007199254740993L, settings.Get(Threshold));
            Assert.Equal(6, settings.Get(Width));
            Assert.Throws<ArgumentNullException>(() => settings.Get<bool>(null!));
        }

        [Fact]
        public void A_value_that_does_not_read_or_that_the_key_refuses_reads_as_the_default_and_is_reported_once()
        {
            List<(string, SettingEntryProblem)> problems = [];
            TenantSettings settings = Settings(
                new() { [AutoNumber.Name] = Parse("\"yes\""), [Width.Name] = Parse("40") },
                report: (key, problem) => problems.Add((key, problem)));

            Assert.True(settings.Get(AutoNumber));
            Assert.Equal(6, settings.Get(Width));
            Assert.True(settings.Get(AutoNumber));
            Assert.Equal(6, settings.Get(Width));

            Assert.Equal([(AutoNumber.Name, SettingEntryProblem.Invalid), (Width.Name, SettingEntryProblem.Invalid)], problems);
        }

        [Fact]
        public void A_value_is_read_once_and_remembered()
        {
            int checks = 0;
            SettingKey<int> counted = new("gl.posting.counted", 1, validate: _ =>
            {
                checks++;
                return null;
            });
            TenantSettings settings = Settings(new() { [counted.Name] = Parse("5") });

            Assert.Equal(5, settings.Get(counted));
            Assert.Equal(5, settings.Get(counted));
            Assert.Equal(1, checks);
        }

        [Fact]
        public void A_secret_is_decrypted_on_first_read_only()
        {
            int calls = 0;
            TenantSettings settings = Settings(
                new() { [ApiKey.Name] = Parse("\"cipher:\\\"s3cr3t\\\"\"") },
                unprotect: text =>
                {
                    calls++;
                    return text.StartsWith("cipher:", StringComparison.Ordinal) ? text["cipher:".Length..] : null;
                });

            Assert.Equal(0, calls);
            Assert.Equal("s3cr3t", settings.Get(ApiKey));
            Assert.Equal("s3cr3t", settings.Get(ApiKey));
            Assert.Equal(1, calls);
        }

        [Theory]
        [InlineData("\"cipher:\\\"s3cr3t\\\"\"", false, false)]
        [InlineData("\"other\"", true, false)]
        [InlineData("\"other\"", true, true)]
        [InlineData("42", true, false)]
        public void A_secret_that_cannot_be_decrypted_reads_as_the_default_and_is_reported_unreadable(string stored, bool hasProtector, bool throws)
        {
            List<(string, SettingEntryProblem)> problems = [];
            Func<string, string?>? unprotect = hasProtector
                ? text => throws ? throw new CryptographicException("Wrong key ring.") : text.StartsWith("cipher:", StringComparison.Ordinal) ? text[7..] : null
                : null;
            TenantSettings settings = Settings(new() { [ApiKey.Name] = Parse(stored) }, unprotect, (key, problem) => problems.Add((key, problem)));

            Assert.Null(settings.Get(ApiKey));
            Assert.Null(settings.Get(ApiKey));
            Assert.Equal([(ApiKey.Name, SettingEntryProblem.SecretUnreadable)], problems);
        }

        [Fact]
        public void A_decrypted_secret_that_is_not_json_of_the_keys_type_is_reported_invalid()
        {
            List<(string, SettingEntryProblem)> problems = [];
            TenantSettings settings = Settings(
                new() { [ApiKey.Name] = Parse("\"not json\""), ["acme.erp.port"] = Parse("\"\\\"x\\\"\"") },
                static text => text,
                (key, problem) => problems.Add((key, problem)));

            Assert.Null(settings.Get(ApiKey));
            Assert.Equal(1234, settings.Get(new SettingKey<int>("acme.erp.port", 1234, visibility: SettingVisibility.Secret)));
            Assert.Equal([(ApiKey.Name, SettingEntryProblem.Invalid), ("acme.erp.port", SettingEntryProblem.Invalid)], problems);
        }

        [Theory]
        [InlineData("2026-03-15T20:59:59Z", "Asia/Riyadh", "2026-03-15")]
        [InlineData("2026-03-15T21:00:00Z", "Asia/Riyadh", "2026-03-16")]
        [InlineData("2026-03-15T03:59:59Z", "America/New_York", "2026-03-14")]
        [InlineData("2026-03-15T04:00:00Z", "America/New_York", "2026-03-15")]
        [InlineData("2026-01-15T04:59:59Z", "America/New_York", "2026-01-14")]
        [InlineData("2026-03-15T23:59:59Z", "UTC", "2026-03-15")]
        public void Today_is_the_clocks_instant_on_the_tenant_zones_wall_clock(string instant, string zone, string expected)
        {
            // New York observes daylight saving from 8 March 2026, so midnight there is 04:00 UTC in
            // March and 05:00 UTC in January.
            FakeTimeProvider clock = new(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));
            TenantSettings settings = Settings([]) with { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(zone) };

            Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), settings.Today(clock));
            Assert.Throws<ArgumentNullException>(() => settings.Today(null!));
        }

        [Fact]
        public void A_copy_starts_with_an_empty_memo_and_equality_ignores_the_memo()
        {
            TenantSettings original = Settings(new() { [Width.Name] = Parse("8") });
            Assert.Equal(8, original.Get(Width));

            TenantSettings copy = original with { };
            Assert.Equal(original, copy);
            Assert.Equal(original.GetHashCode(), copy.GetHashCode());

            TenantSettings changed = original with { Entries = new Dictionary<string, JsonElement> { [Width.Name] = Parse("9") } };
            Assert.Equal(9, changed.Get(Width));
            Assert.Equal(8, original.Get(Width));
            Assert.NotEqual(original, changed);
        }

        /// <summary>Settings for tenant 7 with given entries.</summary>
        /// <param name="entries">The stored entries.</param>
        /// <param name="unprotect">The decryption, if any.</param>
        /// <param name="report">The problem sink, if any.</param>
        /// <returns>The settings.</returns>
        private static TenantSettings Settings(
            Dictionary<string, JsonElement> entries, Func<string, string?>? unprotect = null, Action<string, SettingEntryProblem>? report = null)
        {
            return new TenantSettings
            {
                TenantId = 7,
                Tag = VersionTag.New(),
                Names = ["Acme", "أكمي", null],
                Languages = [new LanguageInfo("en", "English", "E", false), new LanguageInfo("ar", "العربية", "ع", true)],
                Shape = MultilingualShape.PrimaryAndSecondary,
                Calendars = [],
                TimeZone = TimeZoneInfo.Utc,
                SqlServerTimeZoneName = "UTC",
                ModifiedAt = DateTimeOffset.UnixEpoch,
                Entries = entries,
                Unprotect = unprotect,
                ReportProblem = report,
            };
        }

        /// <summary>Parses JSON text into a detached element.</summary>
        /// <param name="json">The text.</param>
        /// <returns>The element.</returns>
        private static JsonElement Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
