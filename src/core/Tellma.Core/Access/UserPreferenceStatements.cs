// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Caching;

namespace Tellma.Core.Access
{
    // The statements are spec 0014 §3.4 and the item rules spec 0018 §3.5. The caller's own bag is
    // written against the connect prologue's @tm_UserId with SqlOptions.ForCaller; another user's
    // binds the target's id as a parameter and names it in a one-row IdList, so the epilogue bumps
    // that user's PreferencesTag either way. Another user's bag is written under an update lock on
    // that user's stamp row, taken first, so it serialises with the owner's own writes (whose
    // re-check holds that row from the batch's head) and with any other writer of that bag. Keys
    // match under the database's case-insensitive collation; a value compares in binary, so a change
    // of case alone is still written.
    /// <summary>
    ///     The writes of a user's preference bag over <see cref="UserPreferenceList" /> rows: setting
    ///     keys, deleting keys, and replacing the whole bag, each one statement appended to a persist
    ///     batch; and the checks of the items in memory before the statement is appended.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Keys compare as the database compares them, ignoring case: <c>nav.pinned</c> and
    ///         <c>nav.Pinned</c> are one key, so a request naming both is a duplicate.
    ///     </para>
    ///     <para>
    ///         The key cap is asserted inside the statement, after the insert, so two concurrent
    ///         writes of one bag cannot pass it together: both hold the bag owner's stamp row under
    ///         an update lock to their commit, the owner's own write from the batch's head and
    ///         another user's write from the statement's first line.
    ///     </para>
    /// </remarks>
    internal static partial class UserPreferenceStatements
    {
        /// <summary>The longest key, the column's width.</summary>
        public const int MaxKeyLength = 128;

        /// <summary>The tables a bag statement writes.</summary>
        public static readonly FrozenSet<TableName> Writes = new[] { VersionTagRegistry.UserPreferences }.ToFrozenSet();

        /// <summary>Whether a key follows the grammar: dotted segments, the first lower-case-led, the rest alphanumeric-led with hyphens, at most <see cref="MaxKeyLength" /> characters.</summary>
        /// <param name="key">The key.</param>
        /// <returns>True when it does.</returns>
        public static bool IsValidKey(string? key)
        {
            return key is { Length: > 0 and <= MaxKeyLength } && KeyGrammar().IsMatch(key);
        }

        /// <summary>
        ///     Checks the items of a set or a replace: each key's grammar, each value's size in UTF-8,
        ///     and no key named twice. The errors land at <c>items[i].key</c> and <c>items[i].value</c>
        ///     under the given root.
        /// </summary>
        /// <param name="items">The items.</param>
        /// <param name="maxValueBytes">The largest value, in bytes of UTF-8.</param>
        /// <param name="errors">Collects the errors.</param>
        /// <param name="itemsPath">The path of the items, such as the arguments' <c>items</c>.</param>
        public static void Check(IReadOnlyList<UserPreferenceList> items, int maxValueBytes, ValidationErrors errors, ValidationPath itemsPath)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(errors);
            ArgumentNullException.ThrowIfNull(itemsPath);
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < items.Count; index++)
            {
                UserPreferenceList item = items[index];
                ValidationPath at = itemsPath.Index(index);
                CheckKey(item.Key, seen, errors, at.Property(nameof(UserPreferenceList.Key)));

                // A null value is the shape's violation; an oversized one the cap's.
                if (item.Value is null)
                {
                    errors.Add(at.Property(nameof(UserPreferenceList.Value)), ValidationCodes.Required, AccessRefusals.Arguments(("property", nameof(UserPreferenceList.Value))));
                }
                else if (Encoding.UTF8.GetByteCount(item.Value) > maxValueBytes)
                {
                    errors.Add(at.Property(nameof(UserPreferenceList.Value)), AccessCodes.PreferenceValueTooLarge, AccessRefusals.Arguments(("maxBytes", maxValueBytes)));
                }
            }
        }

        /// <summary>Checks the keys of a delete: each key's grammar, and no key named twice. The errors land at <c>keys[i]</c> under the given root.</summary>
        /// <param name="keys">The keys.</param>
        /// <param name="errors">Collects the errors.</param>
        /// <param name="keysPath">The path of the keys, such as the arguments' <c>keys</c>.</param>
        public static void CheckKeys(IReadOnlyList<string> keys, ValidationErrors errors, ValidationPath keysPath)
        {
            ArgumentNullException.ThrowIfNull(keys);
            ArgumentNullException.ThrowIfNull(errors);
            ArgumentNullException.ThrowIfNull(keysPath);
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < keys.Count; index++)
            {
                CheckKey(keys[index], seen, errors, keysPath.Index(index));
            }
        }

        /// <summary>
        ///     Appends a set: the keys' values updated where they changed and inserted where absent,
        ///     then the key cap asserted. Keys the items do not name are kept.
        /// </summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="userId">The user whose bag it writes; null for the connected caller's own.</param>
        /// <param name="items">The items, checked by <see cref="Check" />.</param>
        /// <param name="maxKeys">The most keys one bag holds.</param>
        /// <returns>The statement's handle, which carries no result set.</returns>
        public static BatchResult<RawResult> Set(IDataBatch batch, int? userId, IReadOnlyList<UserPreferenceList> items, int maxKeys)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(items);
            SqlIdentifier rows = batch.Tvp(items);
            if (userId is int id)
            {
                // Another user's bag: that user's stamp row is held under an update lock first.
                SqlIdentifier owner = batch.DeclareIdTable();
                return batch.Sql(
                    $"""
                    INSERT INTO {owner} ([Id]) SELECT [UserId] FROM [core].[UserStamps] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = {id};
                    UPDATE P SET [Value] = I.[Value] FROM [core].[UserPreferences] AS P JOIN {rows} AS I ON I.[Key] = P.[Key]
                    WHERE P.[UserId] = {id} AND P.[Value] <> I.[Value] COLLATE Latin1_General_100_BIN2;
                    INSERT [core].[UserPreferences] ([UserId], [Key], [Value])
                    SELECT {id}, I.[Key], I.[Value] FROM {rows} AS I
                    WHERE NOT EXISTS (SELECT 1 FROM [core].[UserPreferences] AS P WHERE P.[UserId] = {id} AND P.[Key] = I.[Key]);
                    IF (SELECT COUNT(*) FROM [core].[UserPreferences] WHERE [UserId] = {id}) > {maxKeys}
                        THROW 50422, N'Users.TooManyPreferenceKeys', 1;
                    """,
                    ForUser(batch, id));
            }

            return batch.Sql(
                $"""
                UPDATE P SET [Value] = I.[Value] FROM [core].[UserPreferences] AS P JOIN {rows} AS I ON I.[Key] = P.[Key]
                WHERE P.[UserId] = @tm_UserId AND P.[Value] <> I.[Value] COLLATE Latin1_General_100_BIN2;
                INSERT [core].[UserPreferences] ([UserId], [Key], [Value])
                SELECT @tm_UserId, I.[Key], I.[Value] FROM {rows} AS I
                WHERE NOT EXISTS (SELECT 1 FROM [core].[UserPreferences] AS P WHERE P.[UserId] = @tm_UserId AND P.[Key] = I.[Key]);
                IF (SELECT COUNT(*) FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId) > {maxKeys}
                    THROW 50422, N'Users.TooManyPreferenceKeys', 1;
                """,
                SqlOptions.ForCaller(Writes));
        }

        /// <summary>Appends a delete of the named keys; a key the bag does not hold is no error.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="userId">The user whose bag it writes; null for the connected caller's own.</param>
        /// <param name="keys">The keys, checked by <see cref="CheckKeys" />.</param>
        /// <returns>The statement's handle, which carries no result set.</returns>
        public static BatchResult<RawResult> Delete(IDataBatch batch, int? userId, IReadOnlyList<string> keys)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(keys);

            // The keys ride the bag's own table type; a delete reads only their keys.
            SqlIdentifier rows = batch.Tvp([.. keys.Select(static key => new UserPreferenceList { Key = key, Value = string.Empty })]);
            if (userId is int id)
            {
                // Another user's bag: that user's stamp row is held under an update lock first.
                SqlIdentifier owner = batch.DeclareIdTable();
                return batch.Sql(
                    $"""
                    INSERT INTO {owner} ([Id]) SELECT [UserId] FROM [core].[UserStamps] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = {id};
                    DELETE P FROM [core].[UserPreferences] AS P
                    WHERE P.[UserId] = {id} AND EXISTS (SELECT 1 FROM {rows} AS I WHERE I.[Key] = P.[Key]);
                    """,
                    ForUser(batch, id));
            }

            return batch.Sql(
                $"""
                DELETE P FROM [core].[UserPreferences] AS P
                WHERE P.[UserId] = @tm_UserId AND EXISTS (SELECT 1 FROM {rows} AS I WHERE I.[Key] = P.[Key]);
                """,
                SqlOptions.ForCaller(Writes));
        }

        /// <summary>
        ///     Appends a replace of the whole bag: keys the items do not name deleted, the rest updated
        ///     where they changed and inserted where absent, then the key cap asserted.
        /// </summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="userId">The user whose bag it writes; null for the connected caller's own.</param>
        /// <param name="items">The items, checked by <see cref="Check" />.</param>
        /// <param name="maxKeys">The most keys one bag holds.</param>
        /// <returns>The statement's handle, which carries no result set.</returns>
        public static BatchResult<RawResult> Replace(IDataBatch batch, int? userId, IReadOnlyList<UserPreferenceList> items, int maxKeys)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(items);
            SqlIdentifier rows = batch.Tvp(items);
            if (userId is int id)
            {
                // Another user's bag: that user's stamp row is held under an update lock first.
                SqlIdentifier owner = batch.DeclareIdTable();
                return batch.Sql(
                    $"""
                    INSERT INTO {owner} ([Id]) SELECT [UserId] FROM [core].[UserStamps] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = {id};
                    DELETE P FROM [core].[UserPreferences] AS P
                    WHERE P.[UserId] = {id} AND NOT EXISTS (SELECT 1 FROM {rows} AS I WHERE I.[Key] = P.[Key]);
                    UPDATE P SET [Value] = I.[Value] FROM [core].[UserPreferences] AS P JOIN {rows} AS I ON I.[Key] = P.[Key]
                    WHERE P.[UserId] = {id} AND P.[Value] <> I.[Value] COLLATE Latin1_General_100_BIN2;
                    INSERT [core].[UserPreferences] ([UserId], [Key], [Value])
                    SELECT {id}, I.[Key], I.[Value] FROM {rows} AS I
                    WHERE NOT EXISTS (SELECT 1 FROM [core].[UserPreferences] AS P WHERE P.[UserId] = {id} AND P.[Key] = I.[Key]);
                    IF (SELECT COUNT(*) FROM [core].[UserPreferences] WHERE [UserId] = {id}) > {maxKeys}
                        THROW 50422, N'Users.TooManyPreferenceKeys', 1;
                    """,
                    ForUser(batch, id));
            }

            return batch.Sql(
                $"""
                DELETE P FROM [core].[UserPreferences] AS P
                WHERE P.[UserId] = @tm_UserId AND NOT EXISTS (SELECT 1 FROM {rows} AS I WHERE I.[Key] = P.[Key]);
                UPDATE P SET [Value] = I.[Value] FROM [core].[UserPreferences] AS P JOIN {rows} AS I ON I.[Key] = P.[Key]
                WHERE P.[UserId] = @tm_UserId AND P.[Value] <> I.[Value] COLLATE Latin1_General_100_BIN2;
                INSERT [core].[UserPreferences] ([UserId], [Key], [Value])
                SELECT @tm_UserId, I.[Key], I.[Value] FROM {rows} AS I
                WHERE NOT EXISTS (SELECT 1 FROM [core].[UserPreferences] AS P WHERE P.[UserId] = @tm_UserId AND P.[Key] = I.[Key]);
                IF (SELECT COUNT(*) FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId) > {maxKeys}
                    THROW 50422, N'Users.TooManyPreferenceKeys', 1;
                """,
                SqlOptions.ForCaller(Writes));
        }

        /// <summary>The options of a write of another user's bag: that user named in a one-row id table, so the epilogue bumps their tag.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="userId">The user.</param>
        /// <returns>The options.</returns>
        private static SqlOptions ForUser(IDataBatch batch, int userId)
        {
            SqlIdentifier users = batch.Tvp([new IdList { Id = userId }]);
            return new SqlOptions(Writes, userIds: users);
        }

        /// <summary>Checks one key's grammar and that the request names it once.</summary>
        /// <param name="key">The key.</param>
        /// <param name="seen">The keys named so far, ignoring case.</param>
        /// <param name="errors">Collects the errors.</param>
        /// <param name="at">The key's path.</param>
        private static void CheckKey(string? key, HashSet<string> seen, ValidationErrors errors, ValidationPath at)
        {
            if (!IsValidKey(key))
            {
                errors.Add(at, AccessCodes.PreferenceKeyInvalid, AccessRefusals.Arguments(("maxLength", MaxKeyLength)));
            }
            else if (!seen.Add(key!))
            {
                errors.Add(at, AccessCodes.PreferenceKeyDuplicate);
            }
        }

        // \z rather than the grammar's $, which in .NET also matches before a final line feed.
        /// <summary>The key grammar: dotted segments, the first led by a lower-case letter, the rest by a letter or a digit, hyphens after the first character of a later segment.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex(@"^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9][a-zA-Z0-9-]*)*\z", RegexOptions.CultureInvariant)]
        private static partial Regex KeyGrammar();
    }
}
