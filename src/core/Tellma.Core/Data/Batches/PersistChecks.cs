// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Access;
using Tellma.Core.Caching;
using Tellma.Core.Data.TableTypes;

namespace Tellma.Core.Data.Batches
{
    // The re-checks are spec 0012 §5.5 steps 4 and 5, with the guard text of spec 0013 §2.5 and the
    // caller re-check of spec 0014 §7.4.
    /// <summary>
    ///     The re-checks a writing persist batch that carries the connect prologue opens its
    ///     transaction with: the version-tag guard over the tags the batch composed under, then the
    ///     caller re-check. The prologue compared the same tags outside the transaction; a role,
    ///     membership or settings save could have committed since, so the batch compares them again
    ///     under locks it holds to <c>COMMIT</c>.
    /// </summary>
    internal sealed class PersistChecks
    {
        /// <summary>The tenant-level tags' table.</summary>
        public static readonly TableName VersionTags = VersionTagRowConfiguration.Table;

        /// <summary>The per-user tags' table, whose row of the caller the guard reads.</summary>
        public static readonly TableName UserStamps = UserStampConfiguration.Table;

        /// <summary>The users' table, whose row of the caller the re-check reads.</summary>
        public static readonly TableName Users = new("core", "Users");

        /// <summary>
        ///     The expected tag of a name the batch bumps without depending on it: the guard locks the
        ///     row and never compares it. No generated tag is all zeros.
        /// </summary>
        public static readonly Guid NoExpectation = Guid.Empty;

        /// <summary>The expected tag of a name two expectations disagree on: no generated tag is all ones, so the guard fails.</summary>
        public static readonly Guid Conflicting = Guid.AllBitsSet;

        // The tag rows are locked one at a time, in binary name order, by a loop over the expected
        // names (spec 0013 §2.5 gives one EXISTS per lock mode instead). Two reasons:
        // - Each row is read by a seek on its name alone and compared afterwards. A comparison inside
        //   the seek is a residual predicate, and SQL Server releases the REPEATABLEREAD or UPDLOCK
        //   lock of every row such a predicate rejects, which is every row that still matches.
        // - One global order. A batch that bumps any name locks every row it names, its bumped names
        //   among them, under UPDLOCK; one that bumps nothing locks them under REPEATABLEREAD; and
        //   the bump converts its rows in the same order. Two writers then meet at their first
        //   shared name and serialise there, so no two of these batches hold each other's rows. A
        //   settings save and a role save (each reading the other's tag) deadlocked under the
        //   spec's two arms.

        /// <summary>The binary collation every lock loop orders names by, so every batch takes its locks in one order.</summary>
        public const string NameOrder = "Latin1_General_100_BIN2";

        /// <summary>The guard's loop of a batch that bumps no tenant-level name: every row under a shared lock held to COMMIT.</summary>
        private static readonly string SharedLoop = GuardLoop("REPEATABLEREAD");

        /// <summary>The guard's loop of a batch that bumps a tenant-level name: every row under an update lock.</summary>
        private static readonly string UpdateLoop = GuardLoop("UPDLOCK");

        /// <summary>The guard's verdict over the tag rows, which the caller's stamp arm may extend.</summary>
        private static readonly string StaleTest = $"IF {ReservedSqlNames.GuardStale} = 1";

        /// <summary>The guard's failure.</summary>
        private static readonly string StaleThrow = string.Create(
            CultureInfo.InvariantCulture, $"    THROW {TellmaSqlErrors.StaleVersionTag}, N'{TellmaSqlErrors.StaleVersionTagCode}', 1;");

        /// <summary>The caller re-check: a deactivation committed since the prologue voids the write.</summary>
        private static readonly string CallerRecheck = string.Create(
            CultureInfo.InvariantCulture,
            $"IF NOT EXISTS (SELECT 1 FROM {Users.Delimited} WHERE [Id] = {ReservedSqlNames.UserId} AND [IsActive] = 1)\n    THROW {TellmaSqlErrors.CallerInvalid}, N'{TellmaSqlErrors.CallerInvalidCode}', 1;");

        /// <summary>Creates the checks; see <see cref="Compose" />.</summary>
        /// <param name="text">The checks' text.</param>
        /// <param name="expectedTags">The rows <c>@tm_expectedTags</c> binds.</param>
        /// <param name="parameter">The parameter binding them.</param>
        private PersistChecks(string text, IReadOnlyList<VersionTagList> expectedTags, BatchParameter parameter)
        {
            Text = text;
            ExpectedTags = expectedTags;
            Parameters = [parameter];
        }

        /// <summary>The checks' text, emitted after the transaction head's statements and before the body.</summary>
        public string Text { get; }

        /// <summary>The rows the guard's table-valued parameter binds, in name order.</summary>
        public IReadOnlyList<VersionTagList> ExpectedTags { get; }

        /// <summary>The parameters the text binds: <c>@tm_expectedTags</c>.</summary>
        public IReadOnlyList<BatchParameter> Parameters { get; }

        /// <summary>Composes the checks of a writing persist batch that carries the connect prologue.</summary>
        /// <param name="premises">What the connect prologue confirmed.</param>
        /// <param name="dependencies">The cached inputs the batch declared.</param>
        /// <param name="bumps">What the batch bumps, which the bump epilogue moves before the commit.</param>
        /// <param name="binder">The table-type binder.</param>
        /// <returns>The checks.</returns>
        public static PersistChecks Compose(
            GuardPremises premises,
            IReadOnlyList<VersionTagDependency> dependencies,
            VersionTagBumpPlan bumps,
            TableTypeBinder binder)
        {
            ArgumentNullException.ThrowIfNull(premises);
            ArgumentNullException.ThrowIfNull(dependencies);
            ArgumentNullException.ThrowIfNull(bumps);
            ArgumentNullException.ThrowIfNull(binder);

            // --- The names the batch bumps, which the guard reads under an update lock ---
            HashSet<string> bumped = new(bumps.TenantNames, StringComparer.Ordinal);

            // --- The expected tags: settings, permissions for a user, and every dependency ---
            // Two expectations of one name that disagree mean the batch composed under two states of
            // it: the conflicting tag matches no stored tag, so the guard fails and the runner recomposes.
            SortedDictionary<string, Guid> expected = new(StringComparer.Ordinal);
            Expect(expected, VersionTagNames.Settings, premises.Settings);
            if (premises.Permissions is VersionTag permissions)
            {
                Expect(expected, VersionTagNames.Permissions, permissions);
            }

            foreach (VersionTagDependency dependency in dependencies)
            {
                Expect(expected, dependency.Name, dependency.ExpectedTag);
            }

            // --- The names the batch bumps without expecting a tag: locked in the same pass, never compared ---
            foreach (string name in bumps.TenantNames)
            {
                _ = expected.TryAdd(name, NoExpectation);
            }

            List<VersionTagList> rows = [.. expected.Select(pair => new VersionTagList { Name = pair.Key, Tag = pair.Value, Bumped = bumped.Contains(pair.Key) })];
            TableTypeBinding binding = binder.Binding(typeof(VersionTagList));
            TableParameter parameter = new(ReservedSqlNames.ExpectedTags, rows, rows.Count, name => binding.Parameter(name, rows));

            // --- The text: the tag rows in name order, the caller's stamp, then the caller re-check for a user ---
            // Every row under an update lock when the batch bumps a tenant-level name, so that two
            // writers serialise at their first shared name instead of holding shared locks the
            // other's bump must convert.
            List<string> lines = [bumps.TenantNames.Count > 0 ? UpdateLoop : SharedLoop, StaleTest];
            if (!premises.IsSystem)
            {
                // The caller's stamp row after the tag rows, in the order the bump writes them; under
                // an update lock when the batch writes the stamps table, itself or through a
                // user-level bump, so two writes of one stamp row serialise instead of deadlocking on
                // the conversion from a shared lock. Summed rather than filtered, so its lock is held.
                lines.Add(
                    $"   OR (SELECT SUM(IIF(s.[PermissionsTag] = {ReservedSqlNames.ExpectedUserPermissionsTag}, 0, 1)) FROM {UserStamps.Delimited} AS s WITH ({(bumps.WritesUserStamps ? "UPDLOCK" : "REPEATABLEREAD")}, ROWLOCK) WHERE s.[UserId] = {ReservedSqlNames.UserId}) > 0");
            }

            lines.Add(StaleThrow);
            if (!premises.IsSystem)
            {
                lines.Add(CallerRecheck);
            }

            return new PersistChecks(string.Join('\n', lines), rows, parameter);
        }

        /// <summary>Adds an expectation, or marks a name conflicting when two expectations of it disagree.</summary>
        /// <param name="expected">The expectations so far.</param>
        /// <param name="name">The tag's name.</param>
        /// <param name="tag">The tag expected.</param>
        private static void Expect(SortedDictionary<string, Guid> expected, string name, VersionTag tag)
        {
            expected[name] = expected.TryGetValue(name, out Guid known) && known != tag.Value ? Conflicting : tag.Value;
        }

        /// <summary>The guard's loop over the expected names: each row read under the lock mode by a seek on its name, then compared.</summary>
        /// <param name="mode">The lock hint.</param>
        /// <returns>The loop's text.</returns>
        private static string GuardLoop(string mode)
        {
            return string.Join(
                '\n',
                $"DECLARE {ReservedSqlNames.GuardName} nvarchar(128) = (SELECT MIN([Name] COLLATE {NameOrder}) FROM {ReservedSqlNames.ExpectedTags}), {ReservedSqlNames.GuardTag} uniqueidentifier, {ReservedSqlNames.GuardStale} bit = 0;",
                $"WHILE {ReservedSqlNames.GuardName} IS NOT NULL",
                "BEGIN",
                $"    SET {ReservedSqlNames.GuardTag} = (SELECT [Tag] FROM {VersionTags.Delimited} WITH ({mode}, ROWLOCK, FORCESEEK) WHERE [Name] = {ReservedSqlNames.GuardName});",
                $"    IF EXISTS (SELECT 1 FROM {ReservedSqlNames.ExpectedTags} WHERE [Name] = {ReservedSqlNames.GuardName} AND [Tag] <> '{NoExpectation}' AND [Tag] <> {ReservedSqlNames.GuardTag}) SET {ReservedSqlNames.GuardStale} = 1;",
                $"    SET {ReservedSqlNames.GuardName} = (SELECT MIN([Name] COLLATE {NameOrder}) FROM {ReservedSqlNames.ExpectedTags} WHERE [Name] COLLATE {NameOrder} > {ReservedSqlNames.GuardName});",
                "END;");
        }
    }
}
