// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     One tenant's settings, resolved: its content languages and the multilingual shape they
    ///     give, its calendars, zone and default formats, and every stored setting entry. Built by the
    ///     settings loader, cached under the tenant's <see cref="VersionTagNames.Settings" /> tag and
    ///     shared by reference with every request of the tenant, so it is never changed after it is
    ///     built.
    /// </summary>
    /// <remarks>
    ///     Equality compares the members below; the per-entry memo of <see cref="Get{T}" /> takes no
    ///     part, and a copy made with <c>with</c> starts with an empty memo.
    /// </remarks>
    public sealed record TenantSettings
    {
        /// <summary>The values <see cref="Get{T}" /> has read, by key name, each in a box of its key's type.</summary>
        private readonly ConcurrentDictionary<string, object> _memo = new(StringComparer.Ordinal);

        /// <summary>Creates settings; the loader sets every member.</summary>
        public TenantSettings()
        {
        }

        /// <summary>Copies settings for a <c>with</c> expression, with an empty memo.</summary>
        /// <param name="original">The settings copied.</param>
        [SetsRequiredMembers]
        private TenantSettings(TenantSettings original)
        {
            // A record's copy constructor runs no field initializer, so the memo starts here.
            _memo = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
            TenantId = original.TenantId;
            Tag = original.Tag;
            Names = original.Names;
            Languages = original.Languages;
            Shape = original.Shape;
            Calendars = original.Calendars;
            TimeZone = original.TimeZone;
            SqlServerTimeZoneName = original.SqlServerTimeZoneName;
            Region = original.Region;
            Digits = original.Digits;
            ModifiedAt = original.ModifiedAt;
            Entries = original.Entries;
            Unprotect = original.Unprotect;
            ReportProblem = original.ReportProblem;
        }

        /// <summary>The tenant.</summary>
        public required int TenantId { get; init; }

        /// <summary>The <see cref="VersionTagNames.Settings" /> tag these settings are current for.</summary>
        public required VersionTag Tag { get; init; }

        /// <summary>The tenant's display name in each content language, by position 1 to 3 (<c>Name</c>, <c>Name2</c>, <c>Name3</c>); null where unset.</summary>
        public required IReadOnlyList<string?> Names { get; init; }

        /// <summary>The content languages, one to three, in position order.</summary>
        public required IReadOnlyList<LanguageInfo> Languages { get; init; }

        /// <summary>How many content languages there are, which keys the query schema.</summary>
        public required MultilingualShape Shape { get; init; }

        /// <summary>The calendars, one or two, primary first.</summary>
        public required IReadOnlyList<ICalendarSystem> Calendars { get; init; }

        /// <summary>The tenant's zone, which decides what "today" is for every member of the tenant.</summary>
        public required TimeZoneInfo TimeZone { get; init; }

        /// <summary>The tenant's zone as SQL Server names it (its Windows id), computed once per load.</summary>
        public required string SqlServerTimeZoneName { get; init; }

        /// <summary>The tenant's default region below each user's own; null for each language's default.</summary>
        public string? Region { get; init; }

        /// <summary>The tenant's default digits below each user's own; null for the region's default.</summary>
        public DigitStyle? Digits { get; init; }

        /// <summary>When the tenant's typed settings were last changed.</summary>
        public required DateTimeOffset ModifiedAt { get; init; }

        /// <summary>
        ///     The stored setting values by key, undeclared keys dropped; a secret's value as stored,
        ///     encrypted. A key without an entry reads as its default.
        /// </summary>
        public required IReadOnlyDictionary<string, JsonElement> Entries { get; init; }

        /// <summary>
        ///     Decrypts a secret's stored text with the tenant-scoped protector, returning null (or
        ///     throwing <see cref="CryptographicException" />) when it cannot. Bound by the loader; null
        ///     when the host has no data protection, which reads every secret as its default.
        /// </summary>
        public Func<string, string?>? Unprotect { get; init; }

        /// <summary>
        ///     Told, with the key's name, whenever <see cref="Get{T}" /> reads a stored value as its
        ///     default because of a problem, once per key per instance. The loader binds it to the log
        ///     and the invalid-entries counter; no value ever reaches it.
        /// </summary>
        public Action<string, SettingEntryProblem>? ReportProblem { get; init; }

        /// <summary>
        ///     The tenant's value of a setting: the stored value read as <typeparamref name="T" />, else
        ///     the key's default. A secret's stored value is decrypted first. A stored value that does
        ///     not read, that the key's check refuses or that cannot be decrypted is reported and reads
        ///     as the default. Read once per key and remembered.
        /// </summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="key">The setting.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key" /> is null.</exception>
        public T Get<T>(SettingKey<T> key)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (!Entries.TryGetValue(key.Name, out JsonElement stored))
            {
                return key.DefaultValue;
            }

            // A box of another type means another key object shares the name (the registry refuses
            // that); it is read afresh rather than cast.
            if (_memo.TryGetValue(key.Name, out object? remembered) && remembered is StrongBox<T> box)
            {
                return box.Value!;
            }

            T value = Read(key, stored);
            _memo[key.Name] = new StrongBox<T>(value);
            return value;
        }

        /// <summary>The tenant's current date: the clock's instant on the tenant zone's wall clock.</summary>
        /// <param name="clock">The clock.</param>
        /// <returns>The date the query language's <c>today()</c> binds to.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="clock" /> is null.</exception>
        public DateOnly Today(TimeProvider clock)
        {
            ArgumentNullException.ThrowIfNull(clock);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZone).DateTime);
        }

        /// <inheritdoc />
        public bool Equals(TenantSettings? other)
        {
            // Member by member, as a record's own equality compares, leaving out the memo.
            return other is not null
                && TenantId == other.TenantId
                && EqualityComparer<VersionTag>.Default.Equals(Tag, other.Tag)
                && EqualityComparer<IReadOnlyList<string?>>.Default.Equals(Names, other.Names)
                && EqualityComparer<IReadOnlyList<LanguageInfo>>.Default.Equals(Languages, other.Languages)
                && Shape == other.Shape
                && EqualityComparer<IReadOnlyList<ICalendarSystem>>.Default.Equals(Calendars, other.Calendars)
                && EqualityComparer<TimeZoneInfo>.Default.Equals(TimeZone, other.TimeZone)
                && SqlServerTimeZoneName == other.SqlServerTimeZoneName
                && Region == other.Region
                && Digits == other.Digits
                && ModifiedAt == other.ModifiedAt
                && EqualityComparer<IReadOnlyDictionary<string, JsonElement>>.Default.Equals(Entries, other.Entries)
                && EqualityComparer<Func<string, string?>?>.Default.Equals(Unprotect, other.Unprotect)
                && EqualityComparer<Action<string, SettingEntryProblem>?>.Default.Equals(ReportProblem, other.ReportProblem);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            HashCode hash = default;
            hash.Add(TenantId);
            hash.Add(Tag);
            hash.Add(Shape);
            hash.Add(TimeZone);
            hash.Add(SqlServerTimeZoneName);
            hash.Add(Region);
            hash.Add(Digits);
            hash.Add(ModifiedAt);
            return hash.ToHashCode();
        }

        /// <summary>Reads a stored value, falling back to the key's default on any problem.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="key">The setting.</param>
        /// <param name="stored">The stored value.</param>
        /// <returns>The value read, or the default.</returns>
        private T Read<T>(SettingKey<T> key, JsonElement stored)
        {
            JsonElement json = stored;

            // --- A secret is stored as the JSON string of its encrypted JSON ---
            if (key.Visibility == SettingVisibility.Secret)
            {
                string? plaintext = stored.ValueKind == JsonValueKind.String && Unprotect is not null
                    ? TryUnprotect(Unprotect, stored.GetString()!)
                    : null;
                if (plaintext is null)
                {
                    ReportProblem?.Invoke(key.Name, SettingEntryProblem.SecretUnreadable);
                    return key.DefaultValue;
                }

                try
                {
                    using var document = JsonDocument.Parse(plaintext);
                    json = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    ReportProblem?.Invoke(key.Name, SettingEntryProblem.Invalid);
                    return key.DefaultValue;
                }
            }

            // --- The value must read as the key's type and pass the key's check ---
            if (!key.TryRead(json, out T value) || key.Validate?.Invoke(value) is not null)
            {
                ReportProblem?.Invoke(key.Name, SettingEntryProblem.Invalid);
                return key.DefaultValue;
            }

            return value;
        }

        /// <summary>Decrypts a secret, treating a protector's refusal as unreadable.</summary>
        /// <param name="unprotect">The loader's decryption.</param>
        /// <param name="protectedText">The stored text.</param>
        /// <returns>The decrypted JSON, or null.</returns>
        private static string? TryUnprotect(Func<string, string?> unprotect, string protectedText)
        {
            try
            {
                return unprotect(protectedText);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }
}
