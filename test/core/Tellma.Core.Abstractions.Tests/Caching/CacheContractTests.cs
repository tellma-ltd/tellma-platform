// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;

namespace Tellma.Core.Abstractions.Tests.Caching
{
    /// <summary>
    ///     The cache limits and their validation, the format versions of the client-facing shapes, the
    ///     guard's table-type row, and the pinned telemetry names.
    /// </summary>
    public class CacheContractTests
    {
        [Fact]
        public void The_limits_default_as_documented_and_validate()
        {
            CacheOptions options = new();

            Assert.Equal("Tellma:Cache", CacheOptions.SectionPath);
            Assert.Equal(10000, options.SettingsEntries);
            Assert.Equal(50000, options.PreferencesEntries);
            Assert.Equal(50000, options.PermissionsEntries);
            Assert.Equal(2000000L, options.EntityRows);
            Assert.Equal(1000, options.EntityMaxRowsDefault);
            Assert.Equal(10000, options.EntityMaxRowsCeiling);
            Assert.Equal(10000, options.TagSnapshots);
            Assert.Empty(Validate(options));
        }

        [Theory]
        [InlineData(nameof(CacheOptions.SettingsEntries))]
        [InlineData(nameof(CacheOptions.PreferencesEntries))]
        [InlineData(nameof(CacheOptions.PermissionsEntries))]
        [InlineData(nameof(CacheOptions.EntityRows))]
        [InlineData(nameof(CacheOptions.EntityMaxRowsDefault))]
        [InlineData(nameof(CacheOptions.EntityMaxRowsCeiling))]
        [InlineData(nameof(CacheOptions.TagSnapshots))]
        public void Every_limit_is_at_least_1(string limit)
        {
            CacheOptions options = new();
            PropertyInfo property = typeof(CacheOptions).GetProperty(limit)!;
            property.SetValue(options, Convert.ChangeType(0, property.PropertyType, System.Globalization.CultureInfo.InvariantCulture));

            ValidationResult failure = Assert.Single(Validate(options));
            Assert.Contains(limit, failure.MemberNames);
        }

        [Fact]
        public void The_default_row_limit_may_not_exceed_the_ceiling()
        {
            CacheOptions options = new() { EntityMaxRowsDefault = 10001 };

            ValidationResult failure = Assert.Single(Validate(options));
            Assert.Equal([nameof(CacheOptions.EntityMaxRowsDefault), nameof(CacheOptions.EntityMaxRowsCeiling)], failure.MemberNames);
            Assert.Empty(Validate(new CacheOptions { EntityMaxRowsDefault = 10000 }));
        }

        [Fact]
        public void The_client_facing_shapes_start_at_format_version_1()
        {
            Assert.Equal(1, TenantSettingsForClient.FormatVersion);
            Assert.Equal(1, CachedEntitySet<object>.FormatVersion);
        }

        [Fact]
        public void The_guard_row_keys_on_a_name_of_at_most_128_characters()
        {
            PropertyInfo name = typeof(VersionTagList).GetProperty(nameof(VersionTagList.Name))!;

            Assert.NotNull(name.GetCustomAttribute<KeyAttribute>());
            Assert.Equal(128, name.GetCustomAttribute<MaxLengthAttribute>()!.Length);
            Assert.Equal(typeof(Guid), typeof(VersionTagList).GetProperty(nameof(VersionTagList.Tag))!.PropertyType);
            Assert.Equal(typeof(bool), typeof(VersionTagList).GetProperty(nameof(VersionTagList.Bumped))!.PropertyType);
        }

        [Fact]
        public void The_telemetry_names_are_pinned()
        {
            Assert.Equal("Tellma.Core", CacheTelemetryNames.MeterName);
            Assert.Equal("tellma.cache.requests", CacheTelemetryNames.Requests);
            Assert.Equal("tellma.cache.load.duration", CacheTelemetryNames.LoadDuration);
            Assert.Equal("tellma.cache.entries", CacheTelemetryNames.Entries);
            Assert.Equal("tellma.cache.size", CacheTelemetryNames.Size);
            Assert.Equal("tellma.cache.limit", CacheTelemetryNames.Limit);
            Assert.Equal("tellma.cache.evictions", CacheTelemetryNames.Evictions);
            Assert.Equal("tellma.versiontags.bumps", CacheTelemetryNames.Bumps);
            Assert.Equal("tellma.versiontags.stale", CacheTelemetryNames.Stale);
            Assert.Equal("tellma.versiontags.missing", CacheTelemetryNames.Missing);
            Assert.Equal("tellma.settings.entries.invalid", CacheTelemetryNames.InvalidEntries);
            Assert.Equal("cache.kind", CacheTelemetryNames.KindTag);
            Assert.Equal("cache.outcome", CacheTelemetryNames.OutcomeTag);
            Assert.Equal("cache.reason", CacheTelemetryNames.ReasonTag);
            Assert.Equal("tag.kind", CacheTelemetryNames.TagKindTag);
            Assert.Equal("stale.phase", CacheTelemetryNames.StalePhaseTag);

            Assert.Equal("Tellma.Core", LocalizationTelemetryNames.MeterName);
            Assert.Equal("tellma.localization.missing", LocalizationTelemetryNames.Missing);
            Assert.Equal("tellma.localization.headers.rejected", LocalizationTelemetryNames.HeadersRejected);
            Assert.Equal("culture", LocalizationTelemetryNames.CultureTag);
            Assert.Equal("header", LocalizationTelemetryNames.HeaderTag);
        }

        [Fact]
        public void The_outcomes_are_the_requests_counters_tag_values()
        {
            Assert.Equal(["Hit", "Miss", "Stale", "Oversized", "Uncached"], Enum.GetNames<CacheOutcome>());
        }

        /// <summary>Validates options as the startup gate does: every property, then the cross-property rule.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The failures.</returns>
        private static List<ValidationResult> Validate(CacheOptions options)
        {
            List<ValidationResult> results = [];
            _ = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }
    }
}
