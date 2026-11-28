// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Dashboards
{
    /// <summary>
    ///     <c>core.widget-types</c>: every widget type key follows its grammar and starts with its
    ///     feature's name, keys are unique, and every settings type is a concrete class.
    /// </summary>
    /// <remarks>
    ///     Reads the composition alone. The registry keeps a repeated key once, so that this check,
    ///     and not a failing constructor, is where the mistake surfaces.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class WidgetTypesCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.WidgetTypes;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];

            foreach (ContributedWidgetType contributed in record.WidgetTypes)
            {
                // --- The grammar, then the first segment: the feature that registers the key ---
                if (!WidgetTypeKeys.IsWellFormed(contributed.Key))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The widget type '{contributed.Key}' of the feature '{contributed.Owner.Name}' does not have a well-formed key.",
                        $"Key the type as the feature's name, a dot, then lower-case letters and digits starting with a letter, words joined by single hyphens, such as '{contributed.Owner.Name}.cash-position'."));
                }
                else if (WidgetTypeKeys.FeatureOf(contributed.Key) is string feature
                    && !string.Equals(feature, contributed.Owner.Name, StringComparison.Ordinal))
                {
                    string name = contributed.Key[(feature.Length + 1)..];
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The widget type '{contributed.Key}' of the feature '{contributed.Owner.Name}' is not keyed under the feature's name.",
                        $"Start the key with the registering feature's name, such as '{contributed.Owner.Name}.{name}', or register it in the feature '{feature}'."));
                }

                // --- The settings: a closed, concrete class the strict deserializer can create ---
                if (contributed.SettingsType is Type settings
                    && (!settings.IsClass || settings.IsAbstract || settings.ContainsGenericParameters))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The widget type '{contributed.Key}' of the feature '{contributed.Owner.Name}' binds its settings to {TypeDisplay.Qualified(settings)}, which is not a concrete, closed class.",
                        "Bind the settings to a concrete record or class: contribution.WidgetType<TSettings>(key)."));
                }
            }

            // --- Keys are unique, compared ordinally as the registry finds them ---
            foreach (IGrouping<string, ContributedWidgetType> clash in record.WidgetTypes
                .GroupBy(static contributed => contributed.Key, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1))
            {
                string owners = string.Join(" and ", clash.Select(static contributed => $"'{contributed.Owner.Name}'"));
                problems.Add(new CompositionProblem(
                    Name,
                    $"The widget type '{clash.Key}' is registered more than once, by the features {owners}.",
                    "Register each widget type once, in the feature whose key it starts with."));
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
