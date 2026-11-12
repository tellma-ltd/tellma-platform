// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
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
        public static readonly TableName VersionTags = new("core", "VersionTags");

        /// <summary>The per-user tags' table, whose row of the caller the guard reads.</summary>
        public static readonly TableName UserStamps = new("core", "UserStamps");

        /// <summary>The users' table, whose row of the caller the re-check reads.</summary>
        public static readonly TableName Users = new("core", "Users");

        /// <summary>The guard's arm over the expected tags the batch only reads: a shared lock held to COMMIT.</summary>
        private static readonly string ReadArm =
            $"IF EXISTS (SELECT 1 FROM {VersionTags.Delimited} AS t WITH (REPEATABLEREAD, ROWLOCK, FORCESEEK) JOIN {ReservedSqlNames.ExpectedTags} AS e ON e.[Name] = t.[Name] WHERE e.[Bumped] = 0 AND t.[Tag] <> e.[Tag])";

        /// <summary>The guard's arm over the expected tags the batch bumps: an update lock, so two writers of one row serialise.</summary>
        private static readonly string BumpArm =
            $"   OR EXISTS (SELECT 1 FROM {VersionTags.Delimited} AS t WITH (UPDLOCK, ROWLOCK, FORCESEEK) JOIN {ReservedSqlNames.ExpectedTags} AS e ON e.[Name] = t.[Name] WHERE e.[Bumped] = 1 AND t.[Tag] <> e.[Tag])";

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
        /// <param name="bumps">The tenant-level tags the batch bumps explicitly.</param>
        /// <param name="written">The tables the batch's statements declare they write.</param>
        /// <param name="registry">Which tags the written tables bump, when the caching feature is composed.</param>
        /// <param name="binder">The table-type binder.</param>
        /// <returns>The checks.</returns>
        public static PersistChecks Compose(
            GuardPremises premises,
            IReadOnlyList<VersionTagDependency> dependencies,
            IReadOnlySet<string> bumps,
            IReadOnlySet<TableName> written,
            IVersionTagRegistry? registry,
            TableTypeBinder binder)
        {
            ArgumentNullException.ThrowIfNull(premises);
            ArgumentNullException.ThrowIfNull(dependencies);
            ArgumentNullException.ThrowIfNull(bumps);
            ArgumentNullException.ThrowIfNull(written);
            ArgumentNullException.ThrowIfNull(binder);

            // --- The names the batch bumps: its written tables' and its explicit ones; any entity's adds the entities tag ---
            VersionTagEffects effects = registry?.Resolve(written) ?? VersionTagEffects.None;
            HashSet<string> bumped = new(effects.TenantNames, StringComparer.Ordinal);
            bumped.UnionWith(bumps);
            if (bumped.Any(static name => name.StartsWith(VersionTagNames.EntityPrefix, StringComparison.Ordinal)))
            {
                _ = bumped.Add(VersionTagNames.Entities);
            }

            // --- The expected tags: settings, permissions for a user, and every dependency ---
            // Two expectations of one name that disagree mean the batch composed under two states of
            // it: the empty tag matches no stored tag, so the guard fails and the runner recomposes.
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

            List<VersionTagList> rows = [.. expected.Select(pair => new VersionTagList { Name = pair.Key, Tag = pair.Value, Bumped = bumped.Contains(pair.Key) })];
            TableTypeBinding binding = binder.Binding(typeof(VersionTagList));
            TableParameter parameter = new(ReservedSqlNames.ExpectedTags, rows, rows.Count, name => binding.Parameter(name, rows));

            // --- The text: the guard's arms, then the caller re-check for a user ---
            List<string> lines = [ReadArm, BumpArm];
            if (!premises.IsSystem)
            {
                // The caller's stamp row under an update lock when the batch writes it, so two
                // self-writes by one user serialise instead of deadlocking on the lock conversion.
                bool writesStamp = written.Contains(UserStamps)
                    || effects.UserRules.Any(static rule => rule.Column == UserVersionTagNames.Preferences);
                lines.Add(
                    $"   OR EXISTS (SELECT 1 FROM {UserStamps.Delimited} AS s WITH ({(writesStamp ? "UPDLOCK" : "REPEATABLEREAD")}, ROWLOCK) WHERE s.[UserId] = {ReservedSqlNames.UserId} AND s.[PermissionsTag] <> {ReservedSqlNames.ExpectedUserPermissionsTag})");
            }

            lines.Add(StaleThrow);
            if (!premises.IsSystem)
            {
                lines.Add(CallerRecheck);
            }

            return new PersistChecks(string.Join('\n', lines), rows, parameter);
        }

        /// <summary>Adds an expectation, or empties a name's tag when two expectations of it disagree.</summary>
        /// <param name="expected">The expectations so far.</param>
        /// <param name="name">The tag's name.</param>
        /// <param name="tag">The tag expected.</param>
        private static void Expect(SortedDictionary<string, Guid> expected, string name, VersionTag tag)
        {
            expected[name] = expected.TryGetValue(name, out Guid known) && known != tag.Value ? Guid.Empty : tag.Value;
        }
    }
}
