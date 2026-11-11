// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Localization;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.value-sets</c>: every value set is well formed, set names are unique and differ
    ///     from the names of the model's enums, and every property that draws from a set names a
    ///     composed one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A set is well formed when its name is <c>&lt;schema&gt;.&lt;PascalCase&gt;</c>, its
    ///         values are unique without regard to case, each value carries exactly one of a label
    ///         key and a literal label, and each alias maps to one of its values and is none of
    ///         them, without regard to case.
    ///     </para>
    ///     <para>
    ///         Reads the composition alone. The registry keeps the first set of a name and drops a
    ///         set it cannot hold, so that this check, and not a failing constructor, is where a
    ///         mistake surfaces. That a property drawing from a set is a string is the entity
    ///         metadata's rule.
    ///     </para>
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed partial class ValueSetsCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <summary>The feature name the platform's own four sets are reported under.</summary>
        private const string Platform = CoreFeature.FeatureName;

        /// <inheritdoc />
        public string Name => CoreCheckNames.ValueSets;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];

            // --- Every set as composed: the platform's four, then the contributed ones ---
            List<(ValueSetDescriptor Set, string Owner)> sets =
            [
                .. ValueSetRegistry
                    .CoreSets(services.GetRequiredService<ILanguageCatalog>(), services.GetRequiredService<ICalendarRegistry>())
                    .Select(static set => (set, Platform)),
                .. record.ValueSets.Select(static contributed => (contributed.Descriptor, contributed.Owner.Name)),
            ];

            foreach ((ValueSetDescriptor set, string owner) in sets)
            {
                CheckSet(set, owner, problems);
            }

            // --- Names are unique, without regard to case ---
            foreach (IGrouping<string, (ValueSetDescriptor Set, string Owner)> clash in sets
                .Where(static item => !string.IsNullOrEmpty(item.Set.Name))
                .GroupBy(static item => item.Set.Name, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1))
            {
                string owners = string.Join(" and ", clash.Select(static item => $"'{item.Owner}'"));
                problems.Add(new CompositionProblem(
                    Name,
                    $"The value set '{clash.Key}' is contributed more than once, by the features {owners}.",
                    "Contribute each value set once, and name a feature's own sets under its own schema."));
            }

            // --- The model: enum names the sets must differ from, and the properties that draw from a set ---
            // A model that does not build is the entity-metadata check's to report; nothing here can
            // be judged without it.
            if (services.GetService<EntityMetadataSource>()?.Build.Provider is IEntityMetadataProvider metadata)
            {
                CheckModel(metadata, sets, problems);
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>A set name: a lower-case schema, a dot, and a Pascal-case name.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex(@"^[a-z][a-z0-9_]*\.[A-Z][A-Za-z0-9]*\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex SetName();

        /// <summary>Checks one set on its own: its name, its values and its aliases.</summary>
        /// <param name="set">The set.</param>
        /// <param name="owner">The name of the feature that contributed it.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckSet(ValueSetDescriptor set, string owner, List<CompositionProblem> problems)
        {
            const string EntryFix = "Give every value of the set a distinct, non-empty Value and exactly one of LabelKey (a resource key) and Label (a literal that reads the same in every language).";
            string described = $"The value set '{set.Name}' of the feature '{owner}'";

            // --- The name ---
            if (set.Name is null || !SetName().IsMatch(set.Name))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} is not named <schema>.<PascalCase>.",
                    "Name the set by the feature's lower-case schema, a dot and a Pascal-case name, such as 'gl.AccountClass'."));
            }

            // --- The values ---
            if (set.Values is null)
            {
                problems.Add(new CompositionProblem(Name, $"{described} has no list of values.", EntryFix));
                return;
            }

            HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (ValueSetEntry entry in set.Values)
            {
                if (string.IsNullOrEmpty(entry?.Value))
                {
                    problems.Add(new CompositionProblem(Name, $"{described} holds an entry without a value.", EntryFix));
                    continue;
                }

                if (!values.Add(entry.Value))
                {
                    problems.Add(new CompositionProblem(
                        Name, $"{described} holds the value '{entry.Value}' more than once, without regard to case.", EntryFix));
                }

                if ((entry.LabelKey is null) == (entry.Label is null))
                {
                    string both = entry.LabelKey is null ? "neither a label key nor a label" : "both a label key and a label";
                    problems.Add(new CompositionProblem(Name, $"{described} gives the value '{entry.Value}' {both}.", EntryFix));
                }
            }

            // --- The aliases: another spelling of a value, never a value ---
            foreach ((string alias, string target) in set.Aliases ?? new Dictionary<string, string>())
            {
                const string AliasFix = "Map each alias to one of the set's values, and list no value as an alias.";
                if (string.IsNullOrEmpty(alias) || values.Contains(alias))
                {
                    problems.Add(new CompositionProblem(Name, $"{described} lists '{alias}' as an alias, but it is empty or one of the set's values.", AliasFix));
                }

                if (target is null || !values.Contains(target))
                {
                    problems.Add(new CompositionProblem(
                        Name, $"{described} maps the alias '{alias}' to '{target}', which is not one of its values.", AliasFix));
                }
            }
        }

        /// <summary>Checks the sets against the tenant model.</summary>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="sets">Every composed set with its owner.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckModel(IEntityMetadataProvider metadata, List<(ValueSetDescriptor Set, string Owner)> sets, List<CompositionProblem> problems)
        {
            // --- Set names differ from the names of the model's enums ---
            // A C# enum is named on every surface by its entity's schema and its type name
            // (gl.CenterType), the namespace a value set's name lives in (spec 0029 §2.2).
            Dictionary<string, string> enums = new(StringComparer.OrdinalIgnoreCase);
            foreach (EntityMetadata entity in metadata.All)
            {
                foreach (PropertyMetadata property in entity.Properties)
                {
                    Type underlying = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if (underlying.IsEnum)
                    {
                        _ = enums.TryAdd($"{entity.Table.Schema}.{underlying.Name}", TypeDisplay.Qualified(underlying));
                    }
                }
            }

            foreach ((ValueSetDescriptor set, string owner) in sets)
            {
                if (set.Name is not null && enums.TryGetValue(set.Name, out string? enumType))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The value set '{set.Name}' of the feature '{owner}' has the name of the enum {enumType}.",
                        "Rename the value set: a set and an enum are one kind on every surface and share one namespace."));
                }
            }

            // --- Every [ValueSet] property names a composed set, exactly ---
            HashSet<string> names = [.. sets.Where(static item => item.Set.Name is not null).Select(static item => item.Set.Name)];
            foreach (EntityMetadata entity in metadata.All)
            {
                foreach (PropertyMetadata property in entity.Properties)
                {
                    if (property.ValueSet is string name && !names.Contains(name))
                    {
                        problems.Add(new CompositionProblem(
                            Name,
                            $"The entity {TypeDisplay.Qualified(entity.ClrType)} draws {property.Name} from the value set '{name}', which no feature contributes.",
                            $"Name a composed set in [ValueSet] on {property.Name} (set names are case-sensitive), or contribute '{name}' with contribution.ValueSet(descriptor)."));
                    }
                }
            }
        }
    }
}
