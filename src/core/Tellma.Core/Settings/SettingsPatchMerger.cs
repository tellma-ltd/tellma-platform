// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud;
using TypedSettings = Tellma.Core.Abstractions.Settings.Settings;

namespace Tellma.Core.Settings
{
    // The merge is spec 0013 §6.3 step 3, with the codes of §6.5: the typed members and the entries
    // the patch lists are merged into what the validate read found, every refusal collected before
    // any is raised, and nothing is written until all pass. A stale stamp is a conflict, raised only
    // when the patch is otherwise valid. The typed row's stamp is compared here as well as by the
    // persist's emitter, so a stale typed row costs no persist round trip.
    /// <summary>
    ///     Merges a settings patch into the rows its validate read found: the typed row and the
    ///     entries it lists, validated, the secrets protected, the stamps compared.
    /// </summary>
    /// <param name="keys">The setting keys.</param>
    /// <param name="languages">The language catalogue, which the content languages must be offered by.</param>
    /// <param name="calendars">The calendar registry, which the calendars must be composed in.</param>
    /// <param name="valueSets">The value sets, whose time-zone set the zone must be in.</param>
    internal sealed class SettingsPatchMerger(
        ISettingKeyRegistry keys, ILanguageCatalog languages, ICalendarRegistry calendars, IValueSetRegistry valueSets)
    {
        /// <summary>The longest a name may be, as its <c>nvarchar(255)</c> column allows.</summary>
        public const int MaxNameLength = 255;

        /// <summary>The path of the entries' values.</summary>
        private static readonly ValidationPath EntriesPath = ValidationPath.Root.Property(nameof(TenantSettingsPatch.Entries));

        /// <summary>The time-zone set's values by their spelling, without regard to case; built on first use.</summary>
        private readonly Lazy<Dictionary<string, string>> _zones = new(() => Zones(valueSets), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        ///     Merges and validates a patch. Under <see cref="ConcurrencyMode.Check" />, every stamp the
        ///     patch carries is compared with the row read.
        /// </summary>
        /// <param name="patch">The patch.</param>
        /// <param name="fields">Its sorted field mask.</param>
        /// <param name="typed">The typed row as read; null when no typed member is listed, since it is not read then.</param>
        /// <param name="entries">The listed entries' stored rows, by key; a key without a row is absent.</param>
        /// <param name="protector">The tenant's protector of secret values; null in a host without data protection.</param>
        /// <param name="languageIndex">The request's content-language position, which a conflict's modifier is named in.</param>
        /// <returns>What the persist writes.</returns>
        /// <exception cref="ArgumentNullException">An argument other than <paramref name="typed" /> or <paramref name="protector" /> is null.</exception>
        /// <exception cref="ValidationException">The patch names something unknown, or a value it sets is refused.</exception>
        /// <exception cref="ConcurrencyException">A row's stamp is not the one the patch expects, under <c>Check</c>.</exception>
        /// <exception cref="InvalidOperationException">
        ///     A typed member is listed but no typed row was read, or a secret value is set in a host
        ///     without data protection.
        /// </exception>
        public SettingsMerge Merge(
            TenantSettingsPatch patch,
            SettingsPatchFields fields,
            StoredSettings? typed,
            IReadOnlyDictionary<string, StoredEntry> entries,
            IDataProtector? protector,
            int languageIndex)
        {
            ArgumentNullException.ThrowIfNull(patch);
            ArgumentNullException.ThrowIfNull(fields);
            ArgumentNullException.ThrowIfNull(entries);
            List<ValidationError> errors = [];
            List<ConcurrencyConflict> conflicts = [];
            bool check = patch.Concurrency == ConcurrencyMode.Check;

            // --- The fields that name nothing ---
            IReadOnlyList<string> mask = patch.Fields ?? [];
            foreach (int index in fields.Unknown)
            {
                errors.Add(Error(ValidationPath.Root.Property(nameof(TenantSettingsPatch.Fields)).Index(index), SettingsValidationCodes.FieldUnknown, ("field", mask[index])));
            }

            // --- The typed row: merged, validated, its stamp compared ---
            TypedSettings? merged = null;
            bool languagesChanged = false;
            TenantDisplayNames? mirror = null;
            if (fields.ListsTyped)
            {
                StoredSettings stored = typed ?? throw new InvalidOperationException(
                    "The tenant database holds no row of core.Settings. Its migrations seed the row; run the migrator against the database.");
                merged = MergeTyped(patch, fields, stored.Row, errors);
                if (check && patch.ExpectedModifiedAt is not DateTimeOffset expected)
                {
                    errors.Add(Error(ValidationPath.Root.Property(nameof(TenantSettingsPatch.ExpectedModifiedAt)), ValidationCodes.ConcurrencyStampRequired));
                }
                else if (check && patch.ExpectedModifiedAt != stored.Row.ModifiedAt)
                {
                    conflicts.Add(Conflict(stored.Row.Id, stored.Row.ModifiedAt, stored.Row.ModifiedById, stored.Modifier, languageIndex));
                }

                if (merged is not null)
                {
                    // The stamp the emitter compares: the expected one under Check, the stored one otherwise.
                    merged.ModifiedAt = check && patch.ExpectedModifiedAt is DateTimeOffset stamp ? stamp : stored.Row.ModifiedAt;
                    languagesChanged = !LanguagesOf(stored.Row).SequenceEqual(LanguagesOf(merged), StringComparer.Ordinal);
                    // A stored name in a language the tenant does not have is never mirrored, so clearing it changes nothing.
                    TenantDisplayNames names = Mirror(merged);
                    mirror = names.Equals(Mirror(SettingsReads.Entity(stored.Row))) ? null : names;
                }
            }

            // --- The entries: declared, read as their keys, protected when secret, their stamps compared ---
            List<(string Key, string Value)> inserts = [];
            List<SettingEntry> updates = [];
            List<StoredEntry> deletes = [];
            Dictionary<string, string?> changes = new(StringComparer.Ordinal);
            foreach (string name in fields.Keys)
            {
                ValidationPath path = EntriesPath.Property(name);
                if (keys.Find(name) is not SettingKey key)
                {
                    errors.Add(Error(path, SettingsValidationCodes.KeyUndeclared, ("key", name)));
                    continue;
                }

                // --- The value: none resets the key, any other must read as the key's type and pass its check ---
                string? value = null;
                if (patch.Entries is not null && patch.Entries.TryGetValue(name, out JsonElement? sent) && sent is JsonElement json && json.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    if (!key.TryReadValue(json, out _, out string? code))
                    {
                        errors.Add(code is null ? Error(path, SettingsValidationCodes.EntryTypeMismatch, ("key", name)) : Error(path, code));
                        continue;
                    }

                    string text = JsonSerializer.Serialize(json, TellmaJson.Options);
                    value = key.Visibility == SettingVisibility.Secret ? Protect(text, protector, name) : text;
                }

                // --- The stamp, under Check: the row read, or none where none was expected ---
                StoredEntry? row = entries.GetValueOrDefault(name);
                if (check)
                {
                    if (patch.ExpectedEntryStamps is null || !patch.ExpectedEntryStamps.TryGetValue(name, out DateTimeOffset? expected))
                    {
                        errors.Add(Error(path, ValidationCodes.ConcurrencyStampRequired));
                        continue;
                    }

                    if (row is null && expected is not null)
                    {
                        // Deleted under the caller: a conflict, not a not-found, since the editor read it.
                        conflicts.Add(new ConcurrencyConflict(0, null, null, null, IsMissing: true));
                        continue;
                    }

                    if (row is not null && row.ModifiedAt != expected)
                    {
                        conflicts.Add(Conflict(row.Id, row.ModifiedAt, row.ModifiedById, row.Modifier, languageIndex));
                        continue;
                    }
                }

                // --- What the persist does: delete a reset row, insert a new one, update a changed one ---
                if (value is null)
                {
                    if (row is not null)
                    {
                        deletes.Add(row);
                        changes[name] = null;
                    }
                }
                else if (row is null)
                {
                    inserts.Add((name, value));
                    changes[name] = value;
                }
                else if (key.Visibility == SettingVisibility.Secret || !SameJson(row.Value, value))
                {
                    // A secret's ciphertext differs on every save, so it is always written.
                    updates.Add(new SettingEntry { Id = row.Id, Key = row.Key, Value = value, ModifiedAt = row.ModifiedAt });
                    changes[name] = value;
                }
            }

            // --- Every refusal at once; a conflict only when nothing else is refused ---
            return errors.Count > 0
                ? throw new ValidationException(errors)
                : conflicts.Count > 0
                ? throw new ConcurrencyException(PersistFailures.ConflictCode, conflicts)
                : new SettingsMerge(merged, languagesChanged, mirror, inserts.AsReadOnly(), updates.AsReadOnly(), deletes.AsReadOnly(), changes);
        }

        /// <summary>A conflict naming a row's stamp and its modifier.</summary>
        /// <param name="id">The row's id.</param>
        /// <param name="stamp">Its stored stamp.</param>
        /// <param name="modifiedById">Its modifier.</param>
        /// <param name="modifier">The modifier's names; null when that user's row is gone.</param>
        /// <param name="languageIndex">The content-language position the modifier is named in.</param>
        /// <returns>The conflict.</returns>
        internal static ConcurrencyConflict Conflict(long id, DateTimeOffset stamp, int modifiedById, StoredModifier? modifier, int languageIndex)
        {
            return new ConcurrencyConflict(
                id,
                stamp.ToString(PersistFailures.StampFormat, CultureInfo.InvariantCulture),
                modifiedById,
                modifier?.In(languageIndex),
                IsMissing: false);
        }

        /// <summary>The three content-language codes of a row, null where unset.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The codes.</returns>
        private static string?[] LanguagesOf(SettingsRow row)
        {
            return [row.PrimaryLanguage, row.SecondaryLanguage, row.TertiaryLanguage];
        }

        /// <summary>The three content-language codes of a merged row, null where unset.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The codes.</returns>
        private static string?[] LanguagesOf(TypedSettings row)
        {
            return [row.PrimaryLanguage, row.SecondaryLanguage, row.TertiaryLanguage];
        }

        /// <summary>
        ///     The names the catalog mirrors: one per content language, the primary name standing in
        ///     for a language whose own name is unset, since the catalog pairs every language with a name.
        /// </summary>
        /// <param name="merged">The merged row.</param>
        /// <returns>The names.</returns>
        private static TenantDisplayNames Mirror(TypedSettings merged)
        {
            List<string> codes = [merged.PrimaryLanguage];
            if (merged.SecondaryLanguage is string secondary)
            {
                codes.Add(secondary);
                if (merged.TertiaryLanguage is string tertiary)
                {
                    codes.Add(tertiary);
                }
            }

            return new TenantDisplayNames(
                merged.Name,
                codes.Count > 1 ? merged.Name2 ?? merged.Name : null,
                codes.Count > 2 ? merged.Name3 ?? merged.Name : null,
                codes);
        }

        /// <summary>Whether two stored values are the same JSON, compared in the form a save stores.</summary>
        /// <param name="stored">The stored text.</param>
        /// <param name="value">The text to store.</param>
        /// <returns>True when they are the same.</returns>
        private static bool SameJson(string stored, string value)
        {
            try
            {
                using var document = JsonDocument.Parse(stored);
                return string.Equals(JsonSerializer.Serialize(document.RootElement, TellmaJson.Options), value, StringComparison.Ordinal);
            }
            catch (JsonException)
            {
                // A stored value that is not JSON is replaced.
                return false;
            }
        }

        /// <summary>Protects a secret's JSON for the tenant: only the ciphertext, as a JSON string, reaches the row.</summary>
        /// <param name="json">The value's JSON.</param>
        /// <param name="protector">The tenant's protector.</param>
        /// <param name="key">The key, for the message.</param>
        /// <returns>The stored JSON.</returns>
        /// <exception cref="InvalidOperationException">The host has no data protection.</exception>
        private static string Protect(string json, IDataProtector? protector, string key)
        {
            return protector is null
                ? throw new InvalidOperationException(
                    $"The secret setting '{key}' cannot be saved: the host has no data protection, and a secret is stored only protected. Register data protection with a persisted key ring in the host.")
                : JsonSerializer.Serialize(protector.Protect(json), TellmaJson.Options);
        }

        /// <summary>The time-zone set's values by their spelling, without regard to case.</summary>
        /// <param name="valueSets">The value sets.</param>
        /// <returns>The values.</returns>
        /// <exception cref="InvalidOperationException">The platform's time-zone set is not composed.</exception>
        private static Dictionary<string, string> Zones(IValueSetRegistry valueSets)
        {
            ValueSetDescriptor set = valueSets.Find(CoreValueSets.TimeZone)
                ?? throw new InvalidOperationException($"The value set {CoreValueSets.TimeZone}, which the platform composes, is missing.");
            Dictionary<string, string> zones = new(StringComparer.OrdinalIgnoreCase);
            foreach (ValueSetEntry entry in set.Values)
            {
                _ = zones.TryAdd(entry.Value, entry.Value);
            }

            return zones;
        }

        /// <summary>A refusal at a path, with its arguments.</summary>
        /// <param name="path">The path.</param>
        /// <param name="code">The code.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The error.</returns>
        private static ValidationError Error(ValidationPath path, string code, params (string Name, object? Value)[] arguments)
        {
            return new ValidationError(path, code, arguments.ToDictionary(static argument => argument.Name, static argument => argument.Value, StringComparer.Ordinal));
        }

        /// <summary>
        ///     Merges the listed typed members into the stored row: each normalized and checked, then
        ///     the languages and calendars as sets, then the names the new shape leaves out cleared.
        /// </summary>
        /// <param name="patch">The patch.</param>
        /// <param name="fields">Its sorted field mask.</param>
        /// <param name="stored">The stored row.</param>
        /// <param name="errors">Receives the refusals.</param>
        /// <returns>The merged row; null when a refusal leaves it unusable.</returns>
        private TypedSettings? MergeTyped(TenantSettingsPatch patch, SettingsPatchFields fields, SettingsRow stored, List<ValidationError> errors)
        {
            int before = errors.Count;
            bool Listed(string member)
            {
                return fields.Typed.Contains(member);
            }

            static ValidationPath At(string member)
            {
                return ValidationPath.Root.Property(member);
            }

            // --- Each listed member, normalized and checked on its own; a refused one keeps its stored value ---
            string name = Listed(nameof(TenantSettingsPatch.Name)) ? RequiredName(nameof(TenantSettingsPatch.Name), patch.Name, errors) ?? stored.Name : stored.Name;
            string? name2 = Listed(nameof(TenantSettingsPatch.Name2)) ? OptionalName(nameof(TenantSettingsPatch.Name2), patch.Name2, stored.Name2, errors) : stored.Name2;
            string? name3 = Listed(nameof(TenantSettingsPatch.Name3)) ? OptionalName(nameof(TenantSettingsPatch.Name3), patch.Name3, stored.Name3, errors) : stored.Name3;
            string primary = Listed(nameof(TenantSettingsPatch.PrimaryLanguage))
                ? Language(nameof(TenantSettingsPatch.PrimaryLanguage), patch.PrimaryLanguage, required: true, stored.PrimaryLanguage, errors)!
                : stored.PrimaryLanguage;
            string? secondary = Listed(nameof(TenantSettingsPatch.SecondaryLanguage))
                ? Language(nameof(TenantSettingsPatch.SecondaryLanguage), patch.SecondaryLanguage, required: false, stored.SecondaryLanguage, errors)
                : stored.SecondaryLanguage;
            string? tertiary = Listed(nameof(TenantSettingsPatch.TertiaryLanguage))
                ? Language(nameof(TenantSettingsPatch.TertiaryLanguage), patch.TertiaryLanguage, required: false, stored.TertiaryLanguage, errors)
                : stored.TertiaryLanguage;
            string primaryCalendar = Listed(nameof(TenantSettingsPatch.PrimaryCalendar))
                ? Calendar(nameof(TenantSettingsPatch.PrimaryCalendar), patch.PrimaryCalendar, required: true, stored.PrimaryCalendar, errors)!
                : stored.PrimaryCalendar;
            string? secondaryCalendar = Listed(nameof(TenantSettingsPatch.SecondaryCalendar))
                ? Calendar(nameof(TenantSettingsPatch.SecondaryCalendar), patch.SecondaryCalendar, required: false, stored.SecondaryCalendar, errors)
                : stored.SecondaryCalendar;
            string timeZone = Listed(nameof(TenantSettingsPatch.TimeZone)) ? Zone(patch.TimeZone, stored.TimeZone, errors) : stored.TimeZone;
            string? region = Listed(nameof(TenantSettingsPatch.Region)) ? Region(patch.Region, stored.Region, errors) : stored.Region;
            DigitStyle? digits = SettingsReads.Digits(stored.Digits);
            if (Listed(nameof(TenantSettingsPatch.Digits)))
            {
                if (patch.Digits is DigitStyle style && !Enum.IsDefined(style))
                {
                    errors.Add(Error(At(nameof(TenantSettingsPatch.Digits)), ValidationCodes.Invalid, ("property", nameof(TenantSettingsPatch.Digits))));
                }
                else
                {
                    digits = patch.Digits;
                }
            }

            // --- The languages as a list: no gap, no repeat; the refusal at the listed member of a pair ---
            string Blame(string later, string earlier)
            {
                return Listed(later) || !Listed(earlier) ? later : earlier;
            }

            if (tertiary is not null && secondary is null)
            {
                errors.Add(Error(At(Blame(nameof(TenantSettingsPatch.TertiaryLanguage), nameof(TenantSettingsPatch.SecondaryLanguage))), SettingsValidationCodes.TertiaryRequiresSecondary));
            }

            if (secondary is not null && string.Equals(secondary, primary, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(Error(At(Blame(nameof(TenantSettingsPatch.SecondaryLanguage), nameof(TenantSettingsPatch.PrimaryLanguage))), SettingsValidationCodes.LanguagesNotDistinct, ("language", secondary)));
            }

            if (tertiary is not null && (string.Equals(tertiary, primary, StringComparison.OrdinalIgnoreCase) || string.Equals(tertiary, secondary, StringComparison.OrdinalIgnoreCase)))
            {
                string earlier = string.Equals(tertiary, secondary, StringComparison.OrdinalIgnoreCase) ? nameof(TenantSettingsPatch.SecondaryLanguage) : nameof(TenantSettingsPatch.PrimaryLanguage);
                errors.Add(Error(At(Blame(nameof(TenantSettingsPatch.TertiaryLanguage), earlier)), SettingsValidationCodes.LanguagesNotDistinct, ("language", tertiary)));
            }

            // --- The calendars as a pair of two ---
            if (secondaryCalendar is not null && string.Equals(secondaryCalendar, primaryCalendar, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(Error(At(Blame(nameof(TenantSettingsPatch.SecondaryCalendar), nameof(TenantSettingsPatch.PrimaryCalendar))), SettingsValidationCodes.CalendarsNotDistinct));
            }

            if (errors.Count > before)
            {
                return null;
            }

            // --- The shape: a name in a language the tenant no longer has is never shown, so it is cleared ---
            int count = secondary is null ? 1 : tertiary is null ? 2 : 3;
            return new TypedSettings
            {
                Id = stored.Id,
                TenantId = stored.TenantId,
                Name = name,
                Name2 = count > 1 ? name2 : null,
                Name3 = count > 2 ? name3 : null,
                PrimaryLanguage = primary,
                SecondaryLanguage = secondary,
                TertiaryLanguage = tertiary,
                PrimaryCalendar = primaryCalendar,
                SecondaryCalendar = secondaryCalendar,
                TimeZone = timeZone,
                Region = region,
                Digits = digits,
                CreatedAt = stored.CreatedAt,
                CreatedById = stored.CreatedById,
                ModifiedAt = stored.ModifiedAt,
                ModifiedById = stored.ModifiedById,
            };
        }

        /// <summary>The primary name: trimmed, required, within its column.</summary>
        /// <param name="member">The member.</param>
        /// <param name="value">The value sent.</param>
        /// <param name="errors">Receives the refusals.</param>
        /// <returns>The name; null when refused.</returns>
        private static string? RequiredName(string member, string? value, List<ValidationError> errors)
        {
            string? name = Trimmed(value);
            if (name is null)
            {
                errors.Add(Error(ValidationPath.Root.Property(member), ValidationCodes.Required, ("property", member)));
                return null;
            }

            return WithinLength(member, name, errors);
        }

        /// <summary>A name in a later language: trimmed, cleared when blank, within its column.</summary>
        /// <param name="member">The member.</param>
        /// <param name="value">The value sent.</param>
        /// <param name="stored">The stored value, kept when refused.</param>
        /// <param name="errors">Receives the refusals.</param>
        /// <returns>The name, or null.</returns>
        private static string? OptionalName(string member, string? value, string? stored, List<ValidationError> errors)
        {
            string? name = Trimmed(value);
            return name is null ? null : WithinLength(member, name, errors) ?? stored;
        }

        /// <summary>A name no longer than its column.</summary>
        /// <param name="member">The member.</param>
        /// <param name="name">The name.</param>
        /// <param name="errors">Receives the refusals.</param>
        /// <returns>The name; null when refused.</returns>
        private static string? WithinLength(string member, string name, List<ValidationError> errors)
        {
            if (name.Length <= MaxNameLength)
            {
                return name;
            }

            errors.Add(Error(ValidationPath.Root.Property(member), ValidationCodes.MaxLength, ("property", member), ("maxLength", MaxNameLength)));
            return null;
        }

        /// <summary>A content language: offered by the distribution, as the catalogue spells it.</summary>
        /// <param name="member">The member.</param>
        /// <param name="value">The value sent.</param>
        /// <param name="required">Whether a value is required.</param>
        /// <param name="stored">The stored value, kept when refused.</param>
        /// <param name="errors">Receives the refusals.</param>
        /// <returns>The code, or null.</returns>
        private string? Language(string member, string? value, bool required, string? stored, List<ValidationError> errors)
        {
            string? code = Trimmed(value);
            if (code is null)
            {
                if (required)
                {
                    errors.Add(Error(ValidationPath.Root.Property(member), ValidationCodes.Required, ("property", member)));
                    return stored;
                }

                return null;
            }

            if (languages.IsOffered(code) && languages.Find(code) is LanguageInfo language)
            {
                return language.Code;
            }

            errors.Add(Error(ValidationPath.Root.Property(member), SettingsValidationCodes.LanguageNotOffered, ("language", code)));
            return stored;
        }

        /// <summary>A calendar: composed in the registry, as it spells its code.</summary>
        /// <param name="member">The member.</param>
        /// <param name="value">The value sent.</param>
        /// <param name="required">Whether a value is required.</param>
        /// <param name="stored">The stored value, kept when refused.</param>
        /// <param name="errors">Receives the refusals.</param>
        /// <returns>The code, or null.</returns>
        private string? Calendar(string member, string? value, bool required, string? stored, List<ValidationError> errors)
        {
            string? code = Trimmed(value);
            if (code is null)
            {
                if (required)
                {
                    errors.Add(Error(ValidationPath.Root.Property(member), ValidationCodes.Required, ("property", member)));
                    return stored;
                }

                return null;
            }

            if (calendars.TryGet(code) is ICalendarSystem calendar)
            {
                return calendar.Code;
            }

            errors.Add(Error(ValidationPath.Root.Property(member), SettingsValidationCodes.CalendarNotRegistered, ("calendar", code)));
            return stored;
        }

        /// <summary>The zone: required, in the time-zone set, as the set spells it.</summary>
        /// <param name="value">The value sent.</param>
        /// <param name="stored">The stored value, kept when refused.</param>
        /// <param name="errors">Receives the refusals.</param>
        /// <returns>The zone.</returns>
        private string Zone(string? value, string stored, List<ValidationError> errors)
        {
            const string Member = nameof(TenantSettingsPatch.TimeZone);
            string? id = Trimmed(value);
            if (id is null)
            {
                errors.Add(Error(ValidationPath.Root.Property(Member), ValidationCodes.Required, ("property", Member)));
                return stored;
            }

            // An alias of a zone is not in the set: the client maps through the aliases before it sends.
            if (_zones.Value.TryGetValue(id, out string? spelled))
            {
                return spelled;
            }

            errors.Add(Error(ValidationPath.Root.Property(Member), SettingsValidationCodes.TimeZoneUnknown, ("timeZone", id)));
            return stored;
        }

        /// <summary>The region: trimmed and upper-cased, cleared when blank, else offered by an offered language.</summary>
        /// <param name="value">The value sent.</param>
        /// <param name="stored">The stored value, kept when refused.</param>
        /// <param name="errors">Receives the refusals.</param>
        /// <returns>The region, or null.</returns>
        private string? Region(string? value, string? stored, List<ValidationError> errors)
        {
            string? region = Trimmed(value)?.ToUpperInvariant();
            if (region is null || languages.IsRegionOffered(region))
            {
                return region;
            }

            errors.Add(Error(ValidationPath.Root.Property(nameof(TenantSettingsPatch.Region)), SettingsValidationCodes.RegionNotOffered, ("region", region)));
            return stored;
        }

        /// <summary>A value trimmed, null when blank.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The trimmed value, or null.</returns>
        private static string? Trimmed(string? value)
        {
            string? trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }

    /// <summary>What a merged settings patch writes.</summary>
    /// <param name="Typed">The merged typed row to save, carrying the stamp the emitter compares; null when no typed member is listed.</param>
    /// <param name="LanguagesChanged">Whether a content-language column changed, which moves the query schema's shape.</param>
    /// <param name="Mirror">The names the catalog mirrors after the commit; null when neither a name nor the language list changed.</param>
    /// <param name="Inserts">The entries without a row, with their stored JSON.</param>
    /// <param name="Updates">The changed entries' rows, each with its stored stamp.</param>
    /// <param name="Deletes">The reset entries' rows.</param>
    /// <param name="Changes">The stored JSON of every entry the persist writes, by key; null for a reset one.</param>
    internal sealed record SettingsMerge(
        TypedSettings? Typed,
        bool LanguagesChanged,
        TenantDisplayNames? Mirror,
        IReadOnlyList<(string Key, string Value)> Inserts,
        IReadOnlyList<SettingEntry> Updates,
        IReadOnlyList<StoredEntry> Deletes,
        IReadOnlyDictionary<string, string?> Changes);
}
