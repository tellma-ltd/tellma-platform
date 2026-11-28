// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Reflection;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Caching
{
    // The registry is spec 0013 §2.1–§2.4: the declarations the entities carry, the implied names of
    // the cacheable entities, and the fixed rules of the two preference tables that are not entities.
    /// <summary>
    ///     Every version tag a tenant database carries, and which of them a batch's written tables
    ///     bump: built once from the tenant model's entity metadata, with every problem in the
    ///     declarations kept for the startup gate.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A declaration with a problem contributes nothing, so the registry stays usable while the
    ///         gate refuses the composition that holds it.
    ///     </para>
    ///     <para>
    ///         Names compare ordinally, but the column that stores them compares case-insensitively,
    ///         so two names that differ in case alone are refused: they would share one row.
    ///     </para>
    /// </remarks>
    internal sealed partial class VersionTagRegistry : IVersionTagRegistry
    {
        /// <summary>The table of the users' preference bags, which bumps its user's preferences tag.</summary>
        public static readonly TableName UserPreferences = new("core", "UserPreferences");

        /// <summary>The table of the users' notification preferences, which bumps its user's preferences tag.</summary>
        public static readonly TableName NotificationPreferences = new("core", "NotificationPreferences");

        /// <summary>The user-id column of both preference tables.</summary>
        public const string PreferenceUserIdColumn = "UserId";

        /// <summary>The platform's fixed rules for the two preference tables, which are not entities.</summary>
        public static readonly IReadOnlyList<UserVersionTagRule> FixedRules =
        [
            new(UserPreferences, UserVersionTagNames.Preferences, PreferenceUserIdColumn),
            new(NotificationPreferences, UserVersionTagNames.Preferences, PreferenceUserIdColumn),
        ];

        /// <summary>The names every tenant database carries, whatever its entities declare.</summary>
        private static readonly string[] PlatformNames = [VersionTagNames.Settings, VersionTagNames.Permissions, VersionTagNames.Entities];

        /// <summary>Each written table's tenant-level names.</summary>
        private readonly FrozenDictionary<TableName, string[]> _tenantNames;

        /// <summary>Each written table's user-level rules.</summary>
        private readonly FrozenDictionary<TableName, UserVersionTagRule[]> _userRules;

        /// <summary>Builds the registry from the tenant model's entity metadata.</summary>
        /// <param name="metadata">The tenant model's entity metadata.</param>
        public VersionTagRegistry(IEntityMetadataProvider metadata)
            : this((metadata ?? throw new ArgumentNullException(nameof(metadata))).All)
        {
        }

        /// <summary>Builds the registry from entities.</summary>
        /// <param name="entities">Every entity of the tenant model.</param>
        internal VersionTagRegistry(IEnumerable<EntityMetadata> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);

            List<CompositionProblem> problems = [];
            Dictionary<TableName, SortedSet<string>> tenantNames = [];
            Dictionary<TableName, List<UserVersionTagRule>> userRules = [];
            Dictionary<string, string> entityNames = new(StringComparer.Ordinal);

            // --- Each entity's declarations, and the names a cacheable entity implies ---
            foreach (EntityMetadata entity in entities)
            {
                foreach (BumpsVersionTagAttribute declared in entity.ClrType.GetCustomAttributes<BumpsVersionTagAttribute>(inherit: true))
                {
                    if (CheckDeclaredName(entity, declared.Name, problems))
                    {
                        Add(tenantNames, entity.Table, declared.Name);
                    }
                }

                foreach (BumpsUserVersionTagAttribute declared in entity.ClrType.GetCustomAttributes<BumpsUserVersionTagAttribute>(inherit: true))
                {
                    if (UserRule(entity, declared, problems) is UserVersionTagRule rule)
                    {
                        AddRule(userRules, rule);
                    }
                }

                if (entity.IsCacheable && EntityName(entity, entityNames, problems) is string entityName)
                {
                    Add(tenantNames, entity.Table, entityName);
                    Add(tenantNames, entity.Table, VersionTagNames.Entities);
                }
            }

            // --- The fixed rules of the preference tables ---
            foreach (UserVersionTagRule rule in FixedRules)
            {
                AddRule(userRules, rule);
            }

            // --- Every name, the platform's first; two that differ in case alone would share a row ---
            SortedSet<string> names = new(PlatformNames, StringComparer.Ordinal);
            foreach (SortedSet<string> tableNames in tenantNames.Values)
            {
                names.UnionWith(tableNames);
            }

            foreach (IGrouping<string, string> clash in names.GroupBy(static name => name, StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1))
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.VersionTags,
                    $"The version tags {string.Join(" and ", clash.Select(static name => $"'{name}'"))} differ in case alone, and the database stores them in one row.",
                    "Name every version tag in lower case, so no two names differ in case alone."));
            }

            Names = [.. names];
            Problems = problems;
            _tenantNames = tenantNames.ToFrozenDictionary(static pair => pair.Key, static pair => pair.Value.ToArray());
            _userRules = userRules.ToFrozenDictionary(static pair => pair.Key, static pair => pair.Value.ToArray());
        }

        /// <inheritdoc />
        public IReadOnlyList<string> Names { get; }

        /// <summary>Every problem the declarations hold, which the startup gate reports.</summary>
        public IReadOnlyList<CompositionProblem> Problems { get; }

        /// <summary>Whether a name is a plain tenant-level name: lower-case ASCII, starting with a letter, at most 128 characters.</summary>
        /// <param name="name">The name.</param>
        /// <returns>True when it is.</returns>
        public static bool IsPlainName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name.Length <= VersionTagNames.MaxNameLength && PlainName().IsMatch(name);
        }

        /// <inheritdoc />
        public VersionTagEffects Resolve(IReadOnlySet<TableName> writtenTables)
        {
            ArgumentNullException.ThrowIfNull(writtenTables);

            SortedSet<string>? names = null;
            List<UserVersionTagRule>? rules = null;
            foreach (TableName table in writtenTables)
            {
                if (_tenantNames.TryGetValue(table, out string[]? tableNames))
                {
                    (names ??= new(StringComparer.Ordinal)).UnionWith(tableNames);
                }

                if (_userRules.TryGetValue(table, out UserVersionTagRule[]? tableRules))
                {
                    (rules ??= []).AddRange(tableRules);
                }
            }

            // The rules in a stable order, so the epilogue's text is the same for the same writes.
            return names is null && rules is null
                ? VersionTagEffects.None
                : new VersionTagEffects(
                    names is null ? [] : [.. names],
                    rules is null ? [] : [.. rules.OrderBy(static rule => rule.Table.ToString(), StringComparer.Ordinal).ThenBy(static rule => rule.Column)]);
        }

        /// <summary>Checks a <see cref="BumpsVersionTagAttribute" />'s name: a plain name, never an entity name written by hand.</summary>
        /// <param name="entity">The declaring entity.</param>
        /// <param name="name">The declared name.</param>
        /// <param name="problems">Where problems are reported.</param>
        /// <returns>True when the name is valid.</returns>
        private static bool CheckDeclaredName(EntityMetadata entity, string name, List<CompositionProblem> problems)
        {
            if (name.StartsWith(VersionTagNames.EntityPrefix, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.VersionTags,
                    $"The entity {entity.Name} declares the version tag '{name}', but tags named '{VersionTagNames.EntityPrefix}…' belong to cacheable entities and are never declared by hand.",
                    "Remove the declaration; mark the entity [Cacheable] if its list is cached, which gives it its own tag."));
                return false;
            }

            if (!IsPlainName(name))
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.VersionTags,
                    $"The entity {entity.Name} declares the version tag '{name}', which is not a lower-case name of at most {VersionTagNames.MaxNameLength} characters.",
                    "Name the tag with lower-case letters, digits, dots and hyphens, starting with a letter, such as 'currencies'."));
                return false;
            }

            return true;
        }

        /// <summary>The rule a <see cref="BumpsUserVersionTagAttribute" /> declares, when it names an <see cref="int" /> property of its entity.</summary>
        /// <param name="entity">The declaring entity.</param>
        /// <param name="declared">The declaration.</param>
        /// <param name="problems">Where problems are reported.</param>
        /// <returns>The rule, or null when the declaration has a problem.</returns>
        private static UserVersionTagRule? UserRule(EntityMetadata entity, BumpsUserVersionTagAttribute declared, List<CompositionProblem> problems)
        {
            if (!Enum.IsDefined(declared.Column))
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.VersionTags,
                    $"The entity {entity.Name} declares a user-level version tag {(int)declared.Column}, which is not one of {string.Join(", ", Enum.GetNames<UserVersionTagNames>())}.",
                    $"Name the user-level tag with a member of {nameof(UserVersionTagNames)}."));
                return null;
            }

            PropertyMetadata? property = entity.Properties.FirstOrDefault(candidate => candidate.Name == declared.UserIdProperty)
                ?? (entity.Key.Name == declared.UserIdProperty ? entity.Key : null);
            if (property is null || (property.ClrType != typeof(int) && property.ClrType != typeof(int?)))
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.VersionTags,
                    $"The entity {entity.Name} bumps the user-level tag {declared.Column} of the user its property '{declared.UserIdProperty}' names, but it has no mapped int property of that name.",
                    "Name the int property of the entity that holds the affected user's id, such as 'UserId'."));
                return null;
            }

            return new UserVersionTagRule(entity.Table, declared.Column, property.Column);
        }

        /// <summary>A cacheable entity's tag name, unless it is too long or another entity yields it too.</summary>
        /// <param name="entity">The cacheable entity.</param>
        /// <param name="taken">The names given so far, to the entity that took each.</param>
        /// <param name="problems">Where problems are reported.</param>
        /// <returns>The name, or null when it has a problem.</returns>
        private static string? EntityName(EntityMetadata entity, Dictionary<string, string> taken, List<CompositionProblem> problems)
        {
            string name;
            try
            {
                name = VersionTagNames.ForEntity(entity.Name);
            }
            catch (ArgumentException)
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.VersionTags,
                    $"The cacheable entity {entity.Name} would have the version tag '{VersionTagNames.EntityPrefix}{entity.Name}', longer than {VersionTagNames.MaxNameLength} characters.",
                    "Shorten the entity's schema or class name."));
                return null;
            }

            if (!taken.TryAdd(name, TypeDisplay.Qualified(entity.ClrType)))
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.VersionTags,
                    $"The cacheable entities {taken[name]} and {TypeDisplay.Qualified(entity.ClrType)} both have the version tag '{name}'.",
                    "Give each cacheable entity its own name, so each list has its own tag."));
                return null;
            }

            return name;
        }

        /// <summary>Adds a table's tenant-level name.</summary>
        /// <param name="names">Each table's names.</param>
        /// <param name="table">The table.</param>
        /// <param name="name">The name.</param>
        private static void Add(Dictionary<TableName, SortedSet<string>> names, TableName table, string name)
        {
            if (!names.TryGetValue(table, out SortedSet<string>? tableNames))
            {
                names[table] = tableNames = new(StringComparer.Ordinal);
            }

            _ = tableNames.Add(name);
        }

        /// <summary>Adds a table's user-level rule, once.</summary>
        /// <param name="rules">Each table's rules.</param>
        /// <param name="rule">The rule.</param>
        private static void AddRule(Dictionary<TableName, List<UserVersionTagRule>> rules, UserVersionTagRule rule)
        {
            if (!rules.TryGetValue(rule.Table, out List<UserVersionTagRule>? tableRules))
            {
                rules[rule.Table] = tableRules = [];
            }

            if (!tableRules.Contains(rule))
            {
                tableRules.Add(rule);
            }
        }

        /// <summary>The plain-name grammar: a lower-case letter, then lower-case letters, digits, dots and hyphens.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[a-z][a-z0-9.-]*\z", RegexOptions.CultureInvariant)]
        private static partial Regex PlainName();
    }
}
