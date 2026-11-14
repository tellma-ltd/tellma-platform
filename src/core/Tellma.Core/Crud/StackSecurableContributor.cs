// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     Registers the securables of every stack, routed service and child query surface, read from
    ///     the stack registry's descriptors and never from the attributes, each under the feature that
    ///     registered its stack or service. A distribution therefore writes no securable registration
    ///     for its own entities and actions.
    /// </summary>
    /// <param name="registry">The stack registry.</param>
    internal sealed class StackSecurableContributor(IStackRegistry registry) : ISecurableContributor
    {
        /// <inheritdoc />
        public void Contribute(SecurableRegistryBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            foreach (StackDescriptor stack in registry.Stacks)
            {
                foreach ((string action, string? filterRoot) in Securables(stack))
                {
                    _ = builder.Add(stack.Resource, action, filterRoot, feature: stack.Feature);
                }
            }

            // --- Routed services: the securables their actions name, with no filter, since no row is the target ---
            foreach (ApiServiceDescriptor service in registry.Services)
            {
                foreach (SecurableRef securable in service.Actions.Select(static action => action.Securable).OfType<SecurableRef>().Distinct())
                {
                    _ = builder.Add(securable.Resource, securable.Action, filterRoot: null, feature: service.Feature);
                }
            }

            // --- Child query surfaces: Read alone, owned through the navigation to the row above ---
            foreach (ChildQueryDescriptor child in registry.ChildQueries)
            {
                string feature = registry.Find(Root(child))?.Feature ?? throw new InvalidOperationException($"The child query surface of {child.Resource} has no root stack in the registry.");
                _ = builder.Add(child.Resource, AccessActions.Read, child.Resource, owner: new SecurableOwner(child.OwnerResource, child.OwnerNavigation), feature: feature);
            }
        }

        // The standard operations' actions follow spec 0015 §2.4's table.

        /// <summary>
        ///     The securable actions of a stack, each once with its filter root: the standard
        ///     operations', each entity action's and each API action's of the service and its
        ///     companions.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <returns>The distinct actions with their filter roots, in first-registered order.</returns>
        internal static IReadOnlyList<(string Action, string? FilterRoot)> Securables(StackDescriptor stack)
        {
            ArgumentNullException.ThrowIfNull(stack);
            List<(string Action, string? FilterRoot)> securables = [];
            StackOperations operations = stack.Operations;

            // --- The standard operations: one cached list is shared by every reader, so its Read takes no filter ---
            if ((operations & StackOperations.Read) != 0)
            {
                Add(AccessActions.Read, stack.Capabilities.Contains("cacheable") ? null : stack.Resource);
            }

            if ((operations & (StackOperations.Save | StackOperations.Import)) != 0)
            {
                Add(AccessActions.Save, stack.Resource);
            }

            if (operations.HasFlag(StackOperations.Delete))
            {
                Add(AccessActions.Delete, stack.Resource);
            }

            // --- The declared actions: the built-ins' Activate among them ---
            foreach (ActionDescriptor action in stack.Actions)
            {
                switch (action)
                {
                    // Two entity actions sharing an action but not a filter root register both, and
                    // the securables check reports the pair registered with two filter roots.
                    case EntityActionDescriptor entity:
                        Add(entity.Action, entity.SupportsFilter ? stack.Resource : null);
                        break;

                    // An API action loads no target rows, so it requires the pair as registered.
                    case ApiActionDescriptor { Securable: SecurableRef securable } when !securables.Exists(existing => string.Equals(existing.Action, securable.Action, StringComparison.Ordinal)):
                        Add(securable.Action, stack.Resource);
                        break;

                    // A member-only API action requires no securable.
                    default:
                        break;
                }
            }

            return securables;

            void Add(string action, string? filterRoot)
            {
                if (!securables.Contains((action, filterRoot)))
                {
                    securables.Add((action, filterRoot));
                }
            }
        }

        /// <summary>The resource of the stack at the root of a child surface's owner chain.</summary>
        /// <param name="child">The child query surface.</param>
        /// <returns>The root stack's resource.</returns>
        private string Root(ChildQueryDescriptor child)
        {
            string owner = child.OwnerResource;
            while (registry.FindChildQuery(owner) is ChildQueryDescriptor above)
            {
                owner = above.OwnerResource;
            }

            return owner;
        }
    }
}
