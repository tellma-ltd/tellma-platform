// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Settings;

namespace Tellma.Core.Composition.Checks
{
    // The rules are spec 0013 §5.3's table, each a composition problem (§1.3).
    /// <summary>
    ///     <c>core.setting-keys</c>: every setting key of the composition is well declared and
    ///     uniquely named.
    /// </summary>
    /// <remarks>
    ///     <para>A key is well declared when:</para>
    ///     <list type="bullet">
    ///         <item>its field holds a key;</item>
    ///         <item>its name is dotted camelCase of two segments or more, at most 128 characters, and no other key has it;</item>
    ///         <item>its value type is a scalar, an enum, a nullable or array form of one, or the key carries its own serialization contract;</item>
    ///         <item>a key whose type admits null defaults to null, and one whose type does not has a default;</item>
    ///         <item>a secret key defaults to null and references nothing;</item>
    ///         <item>its category is one camelCase segment, spelled as every other key of the category spells it;</item>
    ///         <item>a stated group is dotted camelCase of at most 64 characters;</item>
    ///         <item>a reference names an entity with an <c>int</c>-keyed stack, and the value is an id or a list of ids.</item>
    ///     </list>
    /// </remarks>
    /// <param name="record">The composition's record, whose contributed setting-key types are read.</param>
    internal sealed partial class SettingKeysCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.SettingKeys;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            ImmutableArray<DeclaredSettingKey> declared = DeclaredSettingKey.Discover(record.SettingKeyTypes);

            // --- What resolves a reference: the model and the stacks; a model or registry that does not build is reported elsewhere ---
            IEntityMetadataProvider? metadata = services.GetService<EntityMetadataSource>()?.Build.Provider;
            IStackRegistry? stacks = Stacks(services);

            // --- Each key on its own ---
            foreach (DeclaredSettingKey key in declared)
            {
                if (key.Key is null)
                {
                    problems.Add(Problem(
                        key,
                        "holds no key",
                        "Initialize the field with its key: public static readonly SettingKey<T> Name = new(\"feature.name\", default);"));
                    continue;
                }

                CheckKey(key, key.Key, metadata, stacks, problems);
            }

            // --- Across keys: names are unique, and a category has one spelling ---
            CheckUniqueness(declared, problems);
            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>A key's name: dotted camelCase segments, two or more.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)+\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex KeyName();

        /// <summary>A category: one camelCase segment.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[a-z][a-zA-Z0-9]*\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex CategoryName();

        /// <summary>A stated group: dotted camelCase segments, one or more.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex GroupName();

        /// <summary>The stack registry, or null when none is composed or it does not build (the stack checks report that).</summary>
        /// <param name="services">The root service provider.</param>
        /// <returns>The registry, or null.</returns>
        private static IStackRegistry? Stacks(IServiceProvider services)
        {
            try
            {
                return services.GetService<IStackRegistry>();
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                return null;
            }
        }

        /// <summary>Checks one key's own declaration.</summary>
        /// <param name="declared">The declaration.</param>
        /// <param name="key">The key.</param>
        /// <param name="metadata">The model, or null when it does not build.</param>
        /// <param name="stacks">The stacks, or null when they do not build.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckKey(DeclaredSettingKey declared, SettingKey key, IEntityMetadataProvider? metadata, IStackRegistry? stacks, List<CompositionProblem> problems)
        {
            // --- The name ---
            if (key.Name.Length > SettingKey.MaxNameLength || !KeyName().IsMatch(key.Name))
            {
                problems.Add(Problem(
                    declared,
                    $"is named '{key.Name}', which is not dotted camelCase of two segments or more and at most {SettingKey.MaxNameLength.ToString(CultureInfo.InvariantCulture)} characters",
                    "Name the key '<feature>.<segment>…', each segment a lower-case letter followed by letters and digits: 'gl.posting.autoNumberOnPost'."));
            }

            // --- The visibility ---
            if (!Enum.IsDefined(key.Visibility))
            {
                problems.Add(Problem(declared, $"has the visibility {key.Visibility}, which is not a defined visibility", "Declare the key Server, Client or Secret."));
            }

            // --- The value type, then the default it implies ---
            bool admitted = key.HasOwnContract || SettingValueShape.TryRead(key.ValueType, out _);
            if (!admitted)
            {
                problems.Add(Problem(
                    declared,
                    $"holds values of {key.ValueType.Name}, which is not a type a key holds without a serialization contract of its own",
                    "Use bool, int, long, decimal, string, DateOnly, TimeOnly, Guid or an enum, their nullable forms, or an ImmutableArray of one; or pass the key a JsonTypeInfo<T> from the declaring package's source-generated context."));
            }

            bool defaultIsNull = key.DefaultJson.ValueKind == JsonValueKind.Null;
            if (declared.IsNullable && !defaultIsNull)
            {
                problems.Add(Problem(declared, "admits null but has a default other than null", "Default the key to null, or declare its type without null."));
            }
            else if (!declared.IsNullable && defaultIsNull)
            {
                problems.Add(Problem(declared, "admits no null but defaults to null", "Give the key a default value, or declare its type nullable."));
            }

            // --- A secret: no default, no reference ---
            if (key.Visibility == SettingVisibility.Secret && !defaultIsNull)
            {
                problems.Add(Problem(
                    declared,
                    "is a secret with a default other than null; every key's default is public in the client metadata",
                    "Declare the secret's type nullable and default it to null."));
            }

            if (key.Visibility == SettingVisibility.Secret && key.References is not null)
            {
                problems.Add(Problem(declared, "is a secret that references an entity; a secret references nothing", "Remove the reference, or make the key Server-visible."));
            }

            // --- The category and the group ---
            if (!CategoryName().IsMatch(key.Category))
            {
                problems.Add(Problem(
                    declared,
                    $"is in the category '{key.Category}', which is not one camelCase segment",
                    "Name the category as one segment, a lower-case letter followed by letters and digits, such as 'gl'; or let it default to the key's first segment."));
            }

            if (key.Group is string group && (group.Length > SettingKey.MaxGroupLength || !GroupName().IsMatch(group)))
            {
                problems.Add(Problem(
                    declared,
                    $"is in the group '{group}', which is not dotted camelCase of at most {SettingKey.MaxGroupLength.ToString(CultureInfo.InvariantCulture)} characters",
                    "Name the group as camelCase segments joined by dots, such as 'posting'; or leave it out, so the key's name derives it."));
            }

            // --- The reference ---
            if (key.References is Type references)
            {
                CheckReference(declared, key, references, metadata, stacks, problems);
            }
        }

        /// <summary>Checks that a key references an entity with an <c>int</c>-keyed stack, through an id or a list of ids.</summary>
        /// <param name="declared">The declaration.</param>
        /// <param name="key">The key.</param>
        /// <param name="references">The referenced type.</param>
        /// <param name="metadata">The model, or null when it does not build.</param>
        /// <param name="stacks">The stacks, or null when they do not build.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckReference(
            DeclaredSettingKey declared, SettingKey key, Type references, IEntityMetadataProvider? metadata, IStackRegistry? stacks, List<CompositionProblem> problems)
        {
            if (key.ValueType != typeof(int) && key.ValueType != typeof(int?) && key.ValueType != typeof(ImmutableArray<int>))
            {
                problems.Add(Problem(
                    declared,
                    $"references {references.Name} but holds values of {key.ValueType.Name}",
                    "A key that references an entity holds its id: declare it SettingKey<int>, SettingKey<int?> or SettingKey<ImmutableArray<int>>."));
            }

            // A model or a stack registry that does not build fails its own checks; nothing more to judge here.
            if (metadata is null || stacks is null)
            {
                return;
            }

            // A base resolves to the leaf the distribution registered for it.
            EntityMetadata? entity;
            try
            {
                entity = metadata.Get(references);
            }
            catch (ArgumentException)
            {
                entity = null;
            }

            StackDescriptor? stack = entity is null ? null : stacks.Stacks.FirstOrDefault(candidate => candidate.EntityType == entity.ClrType);
            if (stack is null || stack.KeyType != typeof(int))
            {
                problems.Add(Problem(
                    declared,
                    $"references {references.Name}, which is not an entity with an int-keyed stack",
                    "Reference an entity a feature contributes as a stack keyed by int, such as typeof(Center)."));
            }
        }

        /// <summary>Checks that no two keys share a name, and that a category has one spelling.</summary>
        /// <param name="declared">Every declaration.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckUniqueness(ImmutableArray<DeclaredSettingKey> declared, List<CompositionProblem> problems)
        {
            List<DeclaredSettingKey> keys = [.. declared.Where(static key => key.Key is not null)];
            foreach (IGrouping<string, DeclaredSettingKey> clash in keys.GroupBy(static key => key.Key!.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1))
            {
                string fields = string.Join(" and ", clash.Select(static key => key.Describe));
                problems.Add(new CompositionProblem(
                    Name,
                    $"The setting key '{clash.Key}' is declared more than once, by {fields}.",
                    "Declare each key once, and name a feature's keys under its own first segment."));
            }

            // Securables compare without regard to case, so two spellings would be one tab and one resource.
            foreach (IGrouping<string, DeclaredSettingKey> category in keys.GroupBy(static key => SettingKeyRegistry.CategoryOf(key.Key!), StringComparer.OrdinalIgnoreCase))
            {
                string[] spellings = [.. category.Select(static key => SettingKeyRegistry.CategoryOf(key.Key!)).Distinct(StringComparer.Ordinal)];
                if (spellings.Length > 1)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The setting category '{category.Key}' is spelled {string.Join(" and ", spellings.Select(static spelling => $"'{spelling}'"))}; categories are compared without regard to case.",
                        "Spell the category the same way on every key."));
                }
            }
        }

        /// <summary>A problem with one key's declaration.</summary>
        /// <param name="declared">The declaration.</param>
        /// <param name="problem">What is wrong, after the field's name.</param>
        /// <param name="fix">How to fix it.</param>
        /// <returns>The problem.</returns>
        private CompositionProblem Problem(DeclaredSettingKey declared, string problem, string fix)
        {
            return new CompositionProblem(Name, $"The setting key {declared.Describe} of feature '{declared.Owner.Name}' {problem}.", fix);
        }
    }
}
