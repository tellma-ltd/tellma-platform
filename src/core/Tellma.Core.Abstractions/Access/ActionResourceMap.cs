// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Abstractions.Access
{
    // The implied read of a wildcard-resource grant (spec 0014 §5.2 step 2, narrowed so that it
    // fails closed).
    /// <summary>
    ///     Which resources register each action, frozen from the securable registry. A grant on the
    ///     wildcard resource and another action than <c>Read</c> implies reading only the resources
    ///     that register that action, so a grant that holds an action on one resource never reads
    ///     every other.
    /// </summary>
    /// <remarks>
    ///     Built once per registry and shared by every caller's set. Names compare ignoring case. The
    ///     empty map registers nothing, so a set without one implies no read through the wildcard
    ///     resource.
    /// </remarks>
    public sealed class ActionResourceMap
    {
        /// <summary>The resources of each action.</summary>
        private readonly FrozenDictionary<string, FrozenSet<string>> _resources;

        /// <summary>Freezes the resources of each action.</summary>
        /// <param name="resources">The resources of each action.</param>
        private ActionResourceMap(FrozenDictionary<string, FrozenSet<string>> resources)
        {
            _resources = resources;
        }

        /// <summary>The map that registers nothing.</summary>
        public static ActionResourceMap Empty { get; } = new(FrozenDictionary<string, FrozenSet<string>>.Empty);

        /// <summary>Freezes the resources of each action among some securables, normally the registry's.</summary>
        /// <param name="securables">The securables, such as <see cref="ISecurableRegistry.All" />.</param>
        /// <returns>The map.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="securables" /> is null.</exception>
        /// <exception cref="ArgumentException">A securable is null.</exception>
        public static ActionResourceMap From(IEnumerable<SecurableDescriptor> securables)
        {
            ArgumentNullException.ThrowIfNull(securables);

            // --- Group the resources by action, both ignoring case ---
            Dictionary<string, HashSet<string>> byAction = new(StringComparer.OrdinalIgnoreCase);
            foreach (SecurableDescriptor securable in securables)
            {
                if (securable is null)
                {
                    throw new ArgumentException("The securables must not contain a null item.", nameof(securables));
                }

                if (!byAction.TryGetValue(securable.Action, out HashSet<string>? resources))
                {
                    resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    byAction.Add(securable.Action, resources);
                }

                _ = resources.Add(securable.Resource);
            }

            return new ActionResourceMap(byAction.ToFrozenDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Whether a resource registers an action.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <returns>True when the map holds the pair, ignoring case.</returns>
        public bool Registers(string resource, string action)
        {
            return resource is not null
                && action is not null
                && _resources.TryGetValue(action, out FrozenSet<string>? resources)
                && resources.Contains(resource);
        }
    }
}
