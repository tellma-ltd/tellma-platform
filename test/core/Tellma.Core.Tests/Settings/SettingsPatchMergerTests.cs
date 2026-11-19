// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Settings;
using static Tellma.Core.Tests.Settings.SettingsTestHost;

namespace Tellma.Core.Tests.Settings
{
    /// <summary>
    ///     How a settings patch merges into the rows its validate read found: the field mask, each
    ///     typed member's normalization and refusal, the languages and calendars as sets, the shape,
    ///     the entries' writes, the secrets, and the stamps under each concurrency mode.
    /// </summary>
    public sealed class SettingsPatchMergerTests : IAsyncDisposable
    {
        /// <summary>The typed row's stamp as read.</summary>
        private static readonly DateTimeOffset Stamp = Row().ModifiedAt;

        /// <summary>An entry's stamp as read.</summary>
        private static readonly DateTimeOffset EntryStamp = DateTimeOffset.UnixEpoch.AddDays(3);

        /// <summary>The composition the merger is built over.</summary>
        private readonly SettingsTestHost _host = new();

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _host.DisposeAsync();
        }

        // --- The field mask ---

        [Fact]
        public void The_mask_sorts_typed_members_without_regard_to_case_and_entries_by_key_once_each()
        {
            var fields = SettingsPatchFields.Of(Patch(["name", "TimeZone", "entries.acme.period", "Entries.acme.period", "entries.", "bogus"]));

            Assert.Equal(["Name", "TimeZone"], fields.Typed.Order(StringComparer.Ordinal));
            Assert.Equal(["acme.period"], fields.Keys);
            Assert.Equal([4, 5], fields.Unknown);
        }

        [Fact]
        public void A_field_naming_nothing_is_refused_at_its_position()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["Name", "Nmae"]) with { Name = "Acme" }));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.FieldUnknown, error.Code);
            Assert.Equal("Fields[1]", error.Path.ToString());
            Assert.Equal("Nmae", error.Arguments["field"]);
        }

        // --- The typed members, each on its own ---

        [Theory]
        [InlineData(nameof(TenantSettingsPatch.Name))]
        [InlineData(nameof(TenantSettingsPatch.PrimaryLanguage))]
        [InlineData(nameof(TenantSettingsPatch.PrimaryCalendar))]
        [InlineData(nameof(TenantSettingsPatch.TimeZone))]
        public void Clearing_a_required_member_is_refused(string member)
        {
            TenantSettingsPatch patch = Patch([member]);
            TenantSettingsPatch cleared = member switch
            {
                nameof(TenantSettingsPatch.Name) => patch with { Name = "  " },
                nameof(TenantSettingsPatch.PrimaryLanguage) => patch with { PrimaryLanguage = null },
                nameof(TenantSettingsPatch.PrimaryCalendar) => patch with { PrimaryCalendar = " " },
                _ => patch with { TimeZone = null },
            };

            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(cleared));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(ValidationCodes.Required, error.Code);
            Assert.Equal(member, error.Path.ToString());
            Assert.Equal(member, error.Arguments["property"]);
        }

        [Fact]
        public void Names_are_trimmed_and_a_blank_later_name_is_cleared()
        {
            SettingsMerge merge = Merge(Patch(["Name", "Name2", "SecondaryLanguage"]) with { Name = "  Acme Ltd ", Name2 = " ", SecondaryLanguage = "ar" });

            Assert.Equal("Acme Ltd", merge.Typed!.Name);
            Assert.Null(merge.Typed.Name2);
        }

        [Fact]
        public void A_name_longer_than_its_column_is_refused()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["Name"]) with { Name = new string('a', SettingsPatchMerger.MaxNameLength + 1) }));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(ValidationCodes.MaxLength, error.Code);
            Assert.Equal(SettingsPatchMerger.MaxNameLength, error.Arguments["maxLength"]);
        }

        [Fact]
        public void A_language_the_distribution_does_not_offer_is_refused()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["SecondaryLanguage"]) with { SecondaryLanguage = "xx" }));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.LanguageNotOffered, error.Code);
            Assert.Equal("SecondaryLanguage", error.Path.ToString());
            Assert.Equal("xx", error.Arguments["language"]);
        }

        [Fact]
        public void A_language_is_stored_as_the_catalogue_spells_it()
        {
            SettingsMerge merge = Merge(Patch(["SecondaryLanguage"]) with { SecondaryLanguage = " AR " });

            Assert.Equal("ar", merge.Typed!.SecondaryLanguage);
        }

        [Fact]
        public void A_calendar_no_feature_composes_is_refused()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["SecondaryCalendar"]) with { SecondaryCalendar = "zz" }));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.CalendarNotRegistered, error.Code);
            Assert.Equal("zz", error.Arguments["calendar"]);
        }

        [Fact]
        public void A_zone_outside_the_set_is_refused_and_one_inside_is_stored_as_the_set_spells_it()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["TimeZone"]) with { TimeZone = "Mars/Olympus" }));
            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.TimeZoneUnknown, error.Code);
            Assert.Equal("Mars/Olympus", error.Arguments["timeZone"]);

            SettingsMerge merge = Merge(Patch(["TimeZone"]) with { TimeZone = "asia/riyadh" });
            Assert.Equal("Asia/Riyadh", merge.Typed!.TimeZone);
        }

        [Fact]
        public void A_region_is_trimmed_and_upper_cased_and_refused_unless_an_offered_language_offers_it()
        {
            SettingsMerge merge = Merge(Patch(["Region"]) with { Region = " sa " });
            Assert.Equal("SA", merge.Typed!.Region);

            SettingsMerge cleared = Merge(Patch(["Region"]) with { Region = "  " });
            Assert.Null(cleared.Typed!.Region);

            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["Region"]) with { Region = "zz" }));
            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.RegionNotOffered, error.Code);
            Assert.Equal("ZZ", error.Arguments["region"]);
        }

        [Fact]
        public void Digits_outside_the_styles_are_refused()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["Digits"]) with { Digits = (DigitStyle)99 }));

            Assert.Equal(ValidationCodes.Invalid, Assert.Single(refused.Errors).Code);
        }

        // --- The languages and the calendars as sets ---

        [Fact]
        public void A_language_in_two_positions_is_refused_at_the_later_one()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["SecondaryLanguage"]) with { SecondaryLanguage = "EN" }));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.LanguagesNotDistinct, error.Code);
            Assert.Equal("SecondaryLanguage", error.Path.ToString());
            Assert.Equal("en", error.Arguments["language"]);
        }

        [Fact]
        public void A_repeat_against_a_listed_earlier_position_is_refused_there_when_the_later_one_is_not_listed()
        {
            StoredSettings typed = Typed(Row() with { SecondaryLanguage = "ar" });

            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["PrimaryLanguage"]) with { PrimaryLanguage = "ar" }, typed));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.LanguagesNotDistinct, error.Code);
            Assert.Equal("PrimaryLanguage", error.Path.ToString());
        }

        [Fact]
        public void A_tertiary_language_without_a_secondary_one_is_refused()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["TertiaryLanguage"]) with { TertiaryLanguage = "ar" }));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.TertiaryRequiresSecondary, error.Code);
            Assert.Equal("TertiaryLanguage", error.Path.ToString());
        }

        [Fact]
        public async Task Clearing_the_secondary_language_under_a_stored_tertiary_one_is_refused_at_the_secondary()
        {
            await using SettingsTestHost three = new(languages: ["en", "ar", "am"]);
            StoredSettings typed = Typed(Row() with { SecondaryLanguage = "ar", TertiaryLanguage = "am" });

            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(three, Patch(["SecondaryLanguage"]), typed));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.TertiaryRequiresSecondary, error.Code);
            Assert.Equal("SecondaryLanguage", error.Path.ToString());
        }

        [Fact]
        public void A_secondary_calendar_equal_to_the_primary_is_refused()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["SecondaryCalendar"]) with { SecondaryCalendar = "GC" }));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.CalendarsNotDistinct, error.Code);
            Assert.Equal("SecondaryCalendar", error.Path.ToString());
        }

        [Fact]
        public void Every_refusal_is_reported_at_once()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(
                Patch(["Name", "SecondaryLanguage", "TimeZone", "entries.acme.nowhere"]) with { Name = null, SecondaryLanguage = "xx", TimeZone = "Nowhere" }));

            Assert.Equal(
                [ValidationCodes.Required, SettingsValidationCodes.LanguageNotOffered, SettingsValidationCodes.TimeZoneUnknown, SettingsValidationCodes.KeyUndeclared],
                refused.Errors.Select(static error => error.Code));
        }

        // --- The shape and the mirror ---

        [Fact]
        public void A_shrunk_shape_clears_the_names_it_leaves_out_and_moves_the_languages()
        {
            StoredSettings typed = Typed(Row() with { SecondaryLanguage = "ar" });

            SettingsMerge merge = Merge(Patch(["SecondaryLanguage"]), typed);

            Assert.Null(merge.Typed!.SecondaryLanguage);
            Assert.Null(merge.Typed.Name2);
            Assert.True(merge.LanguagesChanged);
            TenantDisplayNames mirror = Assert.IsType<TenantDisplayNames>(merge.Mirror);
            Assert.Equal(["en"], mirror.Languages);
            Assert.Null(mirror.Name2);
        }

        [Fact]
        public void A_name_change_mirrors_one_name_per_language_the_primary_standing_in_for_an_unset_one()
        {
            SettingsMerge merge = Merge(Patch(["Name", "Name2", "SecondaryLanguage"]) with { Name = "Acme Ltd", Name2 = null, SecondaryLanguage = "ar" });

            Assert.True(merge.LanguagesChanged);
            TenantDisplayNames mirror = Assert.IsType<TenantDisplayNames>(merge.Mirror);
            Assert.Equal("Acme Ltd", mirror.Name);
            Assert.Equal("Acme Ltd", mirror.Name2);
            Assert.Equal(["en", "ar"], mirror.Languages);
        }

        [Fact]
        public void A_save_that_changes_neither_names_nor_languages_mirrors_nothing()
        {
            SettingsMerge merge = Merge(Patch(["TimeZone"]) with { TimeZone = "UTC" });

            Assert.False(merge.LanguagesChanged);
            Assert.Null(merge.Mirror);
        }

        // --- The typed row's stamp ---

        [Fact]
        public void A_typed_member_without_the_expected_stamp_is_refused_under_check()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Patch(["TimeZone"]) with { TimeZone = "UTC", ExpectedModifiedAt = null }));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(ValidationCodes.ConcurrencyStampRequired, error.Code);
            Assert.Equal("ExpectedModifiedAt", error.Path.ToString());
        }

        [Fact]
        public void A_stale_typed_stamp_is_a_conflict_naming_the_modifier_in_the_request_language()
        {
            ConcurrencyException conflict = Assert.Throws<ConcurrencyException>(() => Merge(
                Patch(["TimeZone"]) with { TimeZone = "UTC", ExpectedModifiedAt = Stamp.AddSeconds(-1) }, languageIndex: 2));

            ConcurrencyConflict row = Assert.Single(conflict.Conflicts);
            Assert.Equal(1, row.Id);
            Assert.Equal(1, row.ModifiedById);
            Assert.Equal("عمر", row.ModifiedByName);
            Assert.False(row.IsMissing);
            Assert.NotNull(row.ModifiedAt);
        }

        [Fact]
        public void Override_skips_the_typed_stamp_and_carries_the_stored_one()
        {
            SettingsMerge merge = Merge(Patch(["TimeZone"]) with { TimeZone = "UTC", ExpectedModifiedAt = null, Concurrency = ConcurrencyMode.Override });

            Assert.Equal(Stamp, merge.Typed!.ModifiedAt);
        }

        [Fact]
        public void A_refusal_is_raised_before_a_conflict()
        {
            Assert.Throws<ValidationException>(() => Merge(Patch(["TimeZone"]) with { TimeZone = "Nowhere", ExpectedModifiedAt = Stamp.AddSeconds(-1) }));
        }

        // --- The entries ---

        [Fact]
        public void An_undeclared_key_is_refused()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Entries(("acme.nowhere", Json("1"), null))));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.KeyUndeclared, error.Code);
            Assert.Equal("Entries.acme.nowhere", error.Path.ToString());
            Assert.Equal("acme.nowhere", error.Arguments["key"]);
        }

        [Fact]
        public void A_value_of_the_wrong_type_is_refused()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Entries((SampleKeys.Width.Name, Json("\"wide\""), null))));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(SettingsValidationCodes.EntryTypeMismatch, error.Code);
            Assert.Equal(SampleKeys.Width.Name, error.Arguments["key"]);
        }

        [Fact]
        public void A_value_the_key_refuses_carries_the_key_s_own_code()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(Entries((SampleKeys.Width.Name, Json("40"), null))));

            Assert.Equal("Acme.WidthOutOfRange", Assert.Single(refused.Errors).Code);
        }

        [Fact]
        public void A_listed_entry_without_an_expected_stamp_is_refused_under_check()
        {
            TenantSettingsPatch patch = Patch([$"entries.{SampleKeys.Width.Name}"]) with
            {
                Entries = new Dictionary<string, JsonElement?> { [SampleKeys.Width.Name] = Json("8") },
                ExpectedEntryStamps = new Dictionary<string, DateTimeOffset?>(),
            };

            ValidationException refused = Assert.Throws<ValidationException>(() => Merge(patch));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(ValidationCodes.ConcurrencyStampRequired, error.Code);
            Assert.Equal($"Entries.{SampleKeys.Width.Name}", error.Path.ToString());
        }

        [Fact]
        public void A_new_value_inserts_a_changed_one_updates_an_unchanged_one_writes_nothing_and_a_reset_deletes()
        {
            Dictionary<string, StoredEntry> stored = new(StringComparer.Ordinal)
            {
                [SampleKeys.Period.Name] = Entry(11, SampleKeys.Period.Name, "\"Monthly\""),
                [SampleKeys.Codes.Name] = Entry(12, SampleKeys.Codes.Name, "[ 1, 2 ]"),
                [SampleKeys.AutoNumber.Name] = Entry(13, SampleKeys.AutoNumber.Name, "true"),
            };

            SettingsMerge merge = Merge(
                Entries(
                    (SampleKeys.Width.Name, Json("8"), null),
                    (SampleKeys.Period.Name, Json("\"Quarterly\""), EntryStamp),
                    (SampleKeys.Codes.Name, Json("[1,2]"), EntryStamp),
                    (SampleKeys.AutoNumber.Name, null, EntryStamp)),
                entries: stored);

            Assert.Equal([(SampleKeys.Width.Name, "8")], merge.Inserts);
            SettingEntry update = Assert.Single(merge.Updates);
            Assert.Equal(11, update.Id);
            Assert.Equal("\"Quarterly\"", update.Value);
            Assert.Equal(EntryStamp, update.ModifiedAt);
            Assert.Equal(13, Assert.Single(merge.Deletes).Id);
            Assert.Equal(3, merge.Changes.Count);
            Assert.Null(merge.Changes[SampleKeys.AutoNumber.Name]);
            Assert.Null(merge.Typed);
        }

        [Fact]
        public void A_reset_of_a_key_without_a_row_writes_nothing()
        {
            SettingsMerge merge = Merge(Entries((SampleKeys.Width.Name, null, null)));

            Assert.Empty(merge.Inserts);
            Assert.Empty(merge.Deletes);
            Assert.Empty(merge.Changes);
        }

        [Fact]
        public void An_entry_whose_row_moved_is_a_conflict_naming_its_modifier()
        {
            Dictionary<string, StoredEntry> stored = new(StringComparer.Ordinal) { [SampleKeys.Width.Name] = Entry(21, SampleKeys.Width.Name, "4") };

            ConcurrencyException conflict = Assert.Throws<ConcurrencyException>(() => Merge(
                Entries((SampleKeys.Width.Name, Json("8"), EntryStamp.AddSeconds(-1))), entries: stored));

            ConcurrencyConflict row = Assert.Single(conflict.Conflicts);
            Assert.Equal(21, row.Id);
            Assert.Equal(5, row.ModifiedById);
            Assert.Equal("Farah", row.ModifiedByName);
        }

        [Fact]
        public void An_entry_that_exists_where_none_was_expected_is_a_conflict()
        {
            Dictionary<string, StoredEntry> stored = new(StringComparer.Ordinal) { [SampleKeys.Width.Name] = Entry(21, SampleKeys.Width.Name, "4") };

            ConcurrencyException conflict = Assert.Throws<ConcurrencyException>(() => Merge(Entries((SampleKeys.Width.Name, Json("8"), null)), entries: stored));

            Assert.Equal(21, Assert.Single(conflict.Conflicts).Id);
        }

        [Fact]
        public void An_entry_gone_where_a_stamp_was_expected_is_a_missing_conflict_not_a_not_found()
        {
            ConcurrencyException conflict = Assert.Throws<ConcurrencyException>(() => Merge(Entries((SampleKeys.Width.Name, Json("8"), EntryStamp))));

            ConcurrencyConflict row = Assert.Single(conflict.Conflicts);
            Assert.True(row.IsMissing);
            Assert.Null(row.ModifiedAt);
        }

        [Fact]
        public void Override_skips_the_entry_stamps()
        {
            Dictionary<string, StoredEntry> stored = new(StringComparer.Ordinal) { [SampleKeys.Width.Name] = Entry(21, SampleKeys.Width.Name, "4") };
            TenantSettingsPatch patch = Entries((SampleKeys.Width.Name, Json("8"), null), (SampleKeys.Period.Name, Json("\"Quarterly\""), EntryStamp)) with
            {
                Concurrency = ConcurrencyMode.Override,
                ExpectedEntryStamps = null,
            };

            SettingsMerge merge = Merge(patch, entries: stored);

            Assert.Equal(21, Assert.Single(merge.Updates).Id);
            Assert.Equal([(SampleKeys.Period.Name, "\"Quarterly\"")], merge.Inserts);
        }

        // --- The secrets ---

        [Fact]
        public void A_secret_reaches_the_row_only_protected_for_the_tenant()
        {
            SettingsMerge merge = Merge(Entries((SampleKeys.ApiKey.Name, Json("\"s3cret\""), null)));

            string stored = Assert.Single(merge.Inserts).Value;
            Assert.DoesNotContain("s3cret", stored, StringComparison.Ordinal);
            string ciphertext = JsonSerializer.Deserialize<string>(stored)!;
            Assert.Equal("\"s3cret\"", SettingSecrets.For(_host.Protection, TenantId).Unprotect(ciphertext));
        }

        [Fact]
        public void A_secret_is_rewritten_on_every_save_since_its_ciphertext_cannot_be_compared()
        {
            Dictionary<string, StoredEntry> stored = new(StringComparer.Ordinal) { [SampleKeys.ApiKey.Name] = Entry(31, SampleKeys.ApiKey.Name, _host.Secret("\"s3cret\"")) };

            SettingsMerge merge = Merge(Entries((SampleKeys.ApiKey.Name, Json("\"s3cret\""), EntryStamp)), entries: stored);

            Assert.Equal(31, Assert.Single(merge.Updates).Id);
        }

        [Fact]
        public void A_secret_cannot_be_saved_in_a_host_without_data_protection()
        {
            Assert.Throws<InvalidOperationException>(() => Merge(Entries((SampleKeys.ApiKey.Name, Json("\"s3cret\""), null)), protect: false));
        }

        // --- The helpers ---

        /// <summary>A patch listing the given fields, expecting the typed row's stamp, its values those of the stored row.</summary>
        /// <param name="fields">The field mask.</param>
        /// <returns>The patch.</returns>
        private static TenantSettingsPatch Patch(string[] fields)
        {
            SettingsRow row = Row();
            return new TenantSettingsPatch(
                fields,
                Name: row.Name,
                Name2: row.Name2,
                Name3: row.Name3,
                PrimaryLanguage: row.PrimaryLanguage,
                SecondaryLanguage: null,
                TertiaryLanguage: null,
                PrimaryCalendar: row.PrimaryCalendar,
                SecondaryCalendar: null,
                TimeZone: row.TimeZone,
                ExpectedModifiedAt: Stamp);
        }

        /// <summary>A patch listing entries alone, each with its value (null resets it) and expected stamp.</summary>
        /// <param name="entries">The entries.</param>
        /// <returns>The patch.</returns>
        private static TenantSettingsPatch Entries(params (string Key, JsonElement? Value, DateTimeOffset? Expected)[] entries)
        {
            return new TenantSettingsPatch(
                [.. entries.Select(static entry => TenantSettingsPatch.EntryFieldPrefix + entry.Key)],
                Entries: entries.ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal),
                ExpectedEntryStamps: entries.ToDictionary(static entry => entry.Key, static entry => entry.Expected, StringComparer.Ordinal));
        }

        /// <summary>A JSON value.</summary>
        /// <param name="json">Its text.</param>
        /// <returns>The value.</returns>
        private static JsonElement Json(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        /// <summary>A stored entry modified by Farah at the entry stamp.</summary>
        /// <param name="id">The row's id.</param>
        /// <param name="key">The key.</param>
        /// <param name="value">The stored JSON.</param>
        /// <returns>The entry.</returns>
        private static StoredEntry Entry(int id, string key, string value)
        {
            return new StoredEntry(id, key, value, EntryStamp, 5, new StoredModifier("Farah", "فرح", null));
        }

        /// <summary>A typed row as read, modified by Omar.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The stored row.</returns>
        private static StoredSettings Typed(SettingsRow row)
        {
            return new StoredSettings(row, new StoredModifier("Omar", "عمر", null));
        }

        /// <summary>Merges a patch on the default host.</summary>
        /// <param name="patch">The patch.</param>
        /// <param name="typed">The typed row read; the default row when null.</param>
        /// <param name="entries">The listed entries' rows; none when null.</param>
        /// <param name="protect">Whether the host has data protection.</param>
        /// <param name="languageIndex">The request's content-language position.</param>
        /// <returns>The merge.</returns>
        private SettingsMerge Merge(TenantSettingsPatch patch, StoredSettings? typed = null, IReadOnlyDictionary<string, StoredEntry>? entries = null, bool protect = true, int languageIndex = 1)
        {
            return Merge(_host, patch, typed, entries, protect, languageIndex);
        }

        /// <summary>Merges a patch on a host.</summary>
        /// <param name="host">The host.</param>
        /// <param name="patch">The patch.</param>
        /// <param name="typed">The typed row read; the default row when null.</param>
        /// <param name="entries">The listed entries' rows; none when null.</param>
        /// <param name="protect">Whether the host has data protection.</param>
        /// <param name="languageIndex">The request's content-language position.</param>
        /// <returns>The merge.</returns>
        private static SettingsMerge Merge(
            SettingsTestHost host, TenantSettingsPatch patch, StoredSettings? typed = null, IReadOnlyDictionary<string, StoredEntry>? entries = null, bool protect = true, int languageIndex = 1)
        {
            SettingsPatchMerger merger = new(
                host.Services.GetRequiredService<ISettingKeyRegistry>(),
                host.Services.GetRequiredService<ILanguageCatalog>(),
                host.Services.GetRequiredService<ICalendarRegistry>(),
                host.Services.GetRequiredService<IValueSetRegistry>());
            return merger.Merge(
                patch,
                SettingsPatchFields.Of(patch),
                typed ?? Typed(Row()),
                entries ?? new Dictionary<string, StoredEntry>(StringComparer.Ordinal),
                protect ? SettingSecrets.For(host.Protection, TenantId) : null,
                languageIndex);
        }
    }
}
