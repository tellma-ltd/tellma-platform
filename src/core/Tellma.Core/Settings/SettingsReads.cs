// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.TableTypes;
using TypedSettings = Tellma.Core.Abstractions.Settings.Settings;

namespace Tellma.Core.Settings
{
    // The reads are spec 0013 §6.2's and §6.3's: the editor reads the rows themselves, since the
    // cache holds neither the typed row's audit columns nor a stamp per entry. They are raw
    // statements rather than entity queries: an entity query compiles under the batch's schema
    // shape, and a save that changes the languages must read back the names of the new shape in the
    // same batch. The modifier's names ride the save's validate read, so a conflict found there
    // names who made it without another round trip.
    /// <summary>The settings service's raw reads of the typed row and the stored entries, and the rows they read.</summary>
    internal static class SettingsReads
    {
        /// <summary>Appends the read of the typed row, in the cold load's column order, and its modifier's names: one result set.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The result, after execution.</returns>
        public static BatchResult<RawResult> TypedRow(IDataBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            return batch.Sql(
                $"""
                SELECT S.[Id], S.[TenantId], S.[Name], S.[Name2], S.[Name3], S.[PrimaryLanguage], S.[SecondaryLanguage], S.[TertiaryLanguage],
                       S.[PrimaryCalendar], S.[SecondaryCalendar], S.[TimeZone], S.[Region], S.[Digits], S.[CreatedAt], S.[CreatedById], S.[ModifiedAt], S.[ModifiedById],
                       U.[Name], U.[Name2], U.[Name3]
                  FROM [core].[Settings] AS S LEFT JOIN [core].[Users] AS U ON U.[Id] = S.[ModifiedById];
                """,
                new SqlOptions(resultSets: 1));
        }

        /// <summary>Appends the read of every stored entry, or of the listed keys alone, with each modifier's names: one result set.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="keys">The keys to read; null for every stored entry.</param>
        /// <returns>The result, after execution.</returns>
        public static BatchResult<RawResult> Entries(IDataBatch batch, IReadOnlyCollection<string>? keys)
        {
            ArgumentNullException.ThrowIfNull(batch);
            SqlOptions options = new(resultSets: 1);
            if (keys is null)
            {
                return batch.Sql(
                    $"""
                    SELECT E.[Id], E.[Key], E.[Value], E.[ModifiedAt], E.[ModifiedById], U.[Name], U.[Name2], U.[Name3]
                      FROM [core].[SettingEntries] AS E LEFT JOIN [core].[Users] AS U ON U.[Id] = E.[ModifiedById];
                    """,
                    options);
            }

            // --- The listed keys alone, through a key list the batch binds ---
            SqlIdentifier listed = batch.Tvp([.. keys.Select(static key => new StringList { Id = key })]);
            return batch.Sql(
                $"""
                SELECT E.[Id], E.[Key], E.[Value], E.[ModifiedAt], E.[ModifiedById], U.[Name], U.[Name2], U.[Name3]
                  FROM [core].[SettingEntries] AS E
                  JOIN {listed} AS K ON K.[Id] = E.[Key]
                  LEFT JOIN [core].[Users] AS U ON U.[Id] = E.[ModifiedById];
                """,
                options);
        }

        /// <summary>Reads the typed row from its executed read.</summary>
        /// <param name="result">The read's result.</param>
        /// <returns>The row with its modifier; null in a database the migrations never seeded.</returns>
        /// <exception cref="InvalidOperationException">The table holds more than one row.</exception>
        public static StoredSettings? ReadTypedRow(BatchResult<RawResult> result)
        {
            ArgumentNullException.ThrowIfNull(result);
            QueryRowSet set = result.Value.ResultSets[0];
            if (set.RowCount == 0)
            {
                return null;
            }

            if (set.RowCount > 1)
            {
                throw new InvalidOperationException("The tenant database holds more than one row of core.Settings, which its constraints forbid.");
            }

            // --- One row: the cold load's columns, then the modifier's three names ---
            Columns columns = new(set, 0);
            SettingsRow row = new(
                columns.Int(0),
                columns.Int(1),
                columns.String(2)!,
                columns.String(3),
                columns.String(4),
                columns.String(5)!,
                columns.String(6),
                columns.String(7),
                columns.String(8)!,
                columns.String(9),
                columns.String(10)!,
                columns.String(11),
                columns.String(12),
                columns.Stamp(13),
                columns.Int(14),
                columns.Stamp(15),
                columns.Int(16));
            return new StoredSettings(row, Modifier(columns, 17));
        }

        /// <summary>Reads the stored entries from their executed read.</summary>
        /// <param name="result">The read's result.</param>
        /// <returns>The entries, by key.</returns>
        public static FrozenDictionary<string, StoredEntry> ReadEntries(BatchResult<RawResult> result)
        {
            ArgumentNullException.ThrowIfNull(result);
            QueryRowSet set = result.Value.ResultSets[0];
            Dictionary<string, StoredEntry> entries = new(set.RowCount, StringComparer.Ordinal);
            for (int index = 0; index < set.RowCount; index++)
            {
                Columns columns = new(set, index);
                string key = columns.String(1)!;
                entries[key] = new StoredEntry(columns.Int(0), key, columns.String(2)!, columns.Stamp(3), columns.Int(4), Modifier(columns, 5));
            }

            return entries.ToFrozenDictionary(StringComparer.Ordinal);
        }

        /// <summary>The cold load's view of the rows, which builds the cached settings.</summary>
        /// <param name="typed">The typed row.</param>
        /// <param name="entries">The stored entries.</param>
        /// <returns>The rows.</returns>
        public static SettingsRows ColdLoadRows(StoredSettings? typed, IReadOnlyDictionary<string, StoredEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            return new SettingsRows(typed?.Row, [.. entries.Values.OrderBy(static entry => entry.Id).Select(static entry => KeyValuePair.Create(entry.Key, entry.Value))]);
        }

        /// <summary>The typed row as the editor reads it.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The entity.</returns>
        public static TypedSettings Entity(SettingsRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            return new TypedSettings
            {
                Id = row.Id,
                TenantId = row.TenantId,
                Name = row.Name,
                Name2 = row.Name2,
                Name3 = row.Name3,
                PrimaryLanguage = row.PrimaryLanguage,
                SecondaryLanguage = row.SecondaryLanguage,
                TertiaryLanguage = row.TertiaryLanguage,
                PrimaryCalendar = row.PrimaryCalendar,
                SecondaryCalendar = row.SecondaryCalendar,
                TimeZone = row.TimeZone,
                Region = row.Region,
                Digits = Digits(row.Digits),
                CreatedAt = row.CreatedAt,
                CreatedById = row.CreatedById,
                ModifiedAt = row.ModifiedAt,
                ModifiedById = row.ModifiedById,
            };
        }

        /// <summary>The stored digits as a style; none when the stored value names no style.</summary>
        /// <param name="digits">The stored value.</param>
        /// <returns>The style, or null.</returns>
        public static DigitStyle? Digits(string? digits)
        {
            // By name only, as the cold load reads it: a number that parses as a member is not a stored style.
            return digits is not null && Enum.GetNames<DigitStyle>().Contains(digits, StringComparer.Ordinal) ? Enum.Parse<DigitStyle>(digits) : null;
        }

        /// <summary>The modifier's names at three consecutive columns; null when the user is gone.</summary>
        /// <param name="columns">The row.</param>
        /// <param name="first">The first name's column.</param>
        /// <returns>The names.</returns>
        private static StoredModifier? Modifier(Columns columns, int first)
        {
            return columns.String(first) is string name ? new StoredModifier(name, columns.String(first + 1), columns.String(first + 2)) : null;
        }

        /// <summary>One row of a raw result set, read by column.</summary>
        /// <param name="set">The set.</param>
        /// <param name="row">The row.</param>
        private readonly struct Columns(QueryRowSet set, int row)
        {
            /// <summary>An integer column.</summary>
            /// <param name="column">The column.</param>
            /// <returns>The value.</returns>
            public int Int(int column)
            {
                return ((int[])set.GetBuffer(column))[row];
            }

            /// <summary>A string column.</summary>
            /// <param name="column">The column.</param>
            /// <returns>The value, or null.</returns>
            public string? String(int column)
            {
                return set.IsNull(column, row) ? null : ((string?[])set.GetBuffer(column))[row];
            }

            /// <summary>A stamp column.</summary>
            /// <param name="column">The column.</param>
            /// <returns>The value.</returns>
            public DateTimeOffset Stamp(int column)
            {
                return ((DateTimeOffset[])set.GetBuffer(column))[row];
            }
        }
    }

    /// <summary>The typed row as a settings read found it.</summary>
    /// <param name="Row">The row.</param>
    /// <param name="Modifier">The names of the user who last modified it; null when that user's row is gone.</param>
    internal sealed record StoredSettings(SettingsRow Row, StoredModifier? Modifier);

    /// <summary>One stored entry as a settings read found it.</summary>
    /// <param name="Id">The row's id.</param>
    /// <param name="Key">The key.</param>
    /// <param name="Value">The stored JSON; a secret's is the JSON string of its protected value.</param>
    /// <param name="ModifiedAt">The row's stamp.</param>
    /// <param name="ModifiedById">The user who last modified it.</param>
    /// <param name="Modifier">That user's names; null when that user's row is gone.</param>
    internal sealed record StoredEntry(int Id, string Key, string Value, DateTimeOffset ModifiedAt, int ModifiedById, StoredModifier? Modifier);

    /// <summary>A modifier's name in each content language, as stored.</summary>
    /// <param name="Name">The name in the first language.</param>
    /// <param name="Name2">The name in the second; null when unset.</param>
    /// <param name="Name3">The name in the third; null when unset.</param>
    internal sealed record StoredModifier(string Name, string? Name2, string? Name3)
    {
        /// <summary>The name in a content language, else in the first.</summary>
        /// <param name="language">The content language's position, 1 to 3.</param>
        /// <returns>The name.</returns>
        public string In(int language)
        {
            string? localized = language switch
            {
                2 => Name2,
                3 => Name3,
                _ => Name,
            };

            return string.IsNullOrEmpty(localized) ? Name : localized;
        }
    }
}
