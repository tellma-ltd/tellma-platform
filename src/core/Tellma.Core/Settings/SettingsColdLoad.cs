// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Settings
{
    // The statements are spec 0013 §5.7's cold load: the typed row, then every stored entry, read
    // after the version-tag prelude so the rows are stamped with the tag it returned.
    /// <summary>
    ///     The two statements that read a tenant's settings, and the reader of their two result sets.
    ///     A loader's batch carries them bare after its prelude; a caller's batch carries them inside
    ///     the connect prologue, which runs them only when the settings are stale or missing.
    /// </summary>
    internal static class SettingsColdLoad
    {
        /// <summary>The two statements: the typed row's columns in a fixed order, then every entry's key and value.</summary>
        public const string Text = """
            SELECT [Id], [TenantId], [Name], [Name2], [Name3], [PrimaryLanguage], [SecondaryLanguage], [TertiaryLanguage],
                   [PrimaryCalendar], [SecondaryCalendar], [TimeZone], [Region], [Digits], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]
              FROM [core].[Settings];
            SELECT [Key], [Value] FROM [core].[SettingEntries];
            """;

        /// <summary>Reads the two result sets the statements return, on which the cursor is about to move.</summary>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The ordinal of the statement that carries them, for the cursor's defect messages.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The typed row, or null when the table holds none, and the entries in the order they came.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cursor" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The table holds more than one row.</exception>
        public static async ValueTask<SettingsRows> ReadAsync(ResultSetCursor cursor, int ordinal, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(cursor);

            // --- The typed row: one, or none in a database the migrations never seeded ---
            DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            SettingsRow? row = null;
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (row is not null)
                {
                    throw new InvalidOperationException("The tenant database holds more than one row of core.Settings, which its constraints forbid.");
                }

                row = new SettingsRow(
                    set.GetInt32(0),
                    set.GetInt32(1),
                    set.GetString(2),
                    StringOrNull(set, 3),
                    StringOrNull(set, 4),
                    set.GetString(5),
                    StringOrNull(set, 6),
                    StringOrNull(set, 7),
                    set.GetString(8),
                    StringOrNull(set, 9),
                    set.GetString(10),
                    StringOrNull(set, 11),
                    StringOrNull(set, 12),
                    set.GetFieldValue<DateTimeOffset>(13),
                    set.GetInt32(14),
                    set.GetFieldValue<DateTimeOffset>(15),
                    set.GetInt32(16));
            }

            // --- Every stored entry ---
            set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            List<KeyValuePair<string, string>> entries = [];
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(new KeyValuePair<string, string>(set.GetString(0), set.GetString(1)));
            }

            cursor.CountRows((row is null ? 0 : 1) + entries.Count);
            return new SettingsRows(row, entries);
        }

        /// <summary>A nullable string column.</summary>
        /// <param name="set">The result set.</param>
        /// <param name="ordinal">The column.</param>
        /// <returns>The value, or null.</returns>
        private static string? StringOrNull(DbDataReader set, int ordinal)
        {
            return set.IsDBNull(ordinal) ? null : set.GetString(ordinal);
        }
    }

    /// <summary>What the cold load read: the typed row and the stored entries.</summary>
    /// <param name="Row">The typed row; null when the table holds none.</param>
    /// <param name="Entries">Each stored entry's key and JSON value, in the order they came.</param>
    internal sealed record SettingsRows(SettingsRow? Row, IReadOnlyList<KeyValuePair<string, string>> Entries);

    /// <summary>The typed row of <c>core.Settings</c> as the cold load reads it.</summary>
    /// <param name="Id">The row's id.</param>
    /// <param name="TenantId">The tenant the database belongs to; zero for the placeholder.</param>
    /// <param name="Name">The display name in the primary language.</param>
    /// <param name="Name2">The display name in the secondary language.</param>
    /// <param name="Name3">The display name in the tertiary language.</param>
    /// <param name="PrimaryLanguage">The primary language's code.</param>
    /// <param name="SecondaryLanguage">The secondary language's code.</param>
    /// <param name="TertiaryLanguage">The tertiary language's code.</param>
    /// <param name="PrimaryCalendar">The primary calendar's code.</param>
    /// <param name="SecondaryCalendar">The secondary calendar's code.</param>
    /// <param name="TimeZone">The tenant's zone, an IANA id.</param>
    /// <param name="Region">The default region.</param>
    /// <param name="Digits">The default digits, as stored.</param>
    /// <param name="CreatedAt">When the row was created.</param>
    /// <param name="CreatedById">Who created it.</param>
    /// <param name="ModifiedAt">When it was last modified: its concurrency stamp.</param>
    /// <param name="ModifiedById">Who last modified it.</param>
    internal sealed record SettingsRow(
        int Id,
        int TenantId,
        string Name,
        string? Name2,
        string? Name3,
        string PrimaryLanguage,
        string? SecondaryLanguage,
        string? TertiaryLanguage,
        string PrimaryCalendar,
        string? SecondaryCalendar,
        string TimeZone,
        string? Region,
        string? Digits,
        DateTimeOffset CreatedAt,
        int CreatedById,
        DateTimeOffset ModifiedAt,
        int ModifiedById);
}
