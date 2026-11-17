// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Crud.Details
{
    // The abilities and the reason are spec 0015 §5.3 step 4; the securable of each is §2.4's.
    /// <summary>
    ///     The abilities the platform's <c>permissions</c> extra answers for a stack, each under its
    ///     securable action: <c>save</c>, <c>delete</c>, <c>activate</c> for both directions, and
    ///     <c>action:&lt;name&gt;</c> per entity action. An ability the stack lacks is absent.
    /// </summary>
    internal static class RecordAbilities
    {
        /// <summary>The ability to save a row.</summary>
        public const string Save = "save";

        /// <summary>The ability to delete a row.</summary>
        public const string Delete = "delete";

        /// <summary>The ability to activate or deactivate a row.</summary>
        public const string Activate = "activate";

        /// <summary>The prefix of an entity action's ability, before the action's name.</summary>
        public const string ActionPrefix = "action:";

        /// <summary>The reason of an ability whose filtered grant does not reach the row.</summary>
        public const string OutsideFilter = "outside-filter";

        /// <summary>The abilities of a stack, in the order the extra lists them.</summary>
        /// <param name="stack">The stack.</param>
        /// <returns>Each ability with the securable action it is granted under.</returns>
        public static IReadOnlyList<RecordAbility> Of(StackDescriptor stack)
        {
            ArgumentNullException.ThrowIfNull(stack);
            List<RecordAbility> abilities = [];

            // --- The standard writes ---
            if (stack.Operations.HasFlag(StackOperations.Save))
            {
                abilities.Add(new RecordAbility(Save, AccessActions.Save, SupportsFilter: true));
            }

            if (stack.Operations.HasFlag(StackOperations.Delete))
            {
                abilities.Add(new RecordAbility(Delete, AccessActions.Delete, SupportsFilter: true));
            }

            // --- The built-in activation, one ability for both directions, then each declared entity action ---
            if (stack.Actions.OfType<EntityActionDescriptor>().FirstOrDefault(static action => action.IsBuiltIn) is EntityActionDescriptor builtIn)
            {
                abilities.Add(new RecordAbility(Activate, builtIn.Action, builtIn.SupportsFilter));
            }

            foreach (EntityActionDescriptor action in stack.Actions.OfType<EntityActionDescriptor>().Where(static action => !action.IsBuiltIn))
            {
                abilities.Add(new RecordAbility(ActionPrefix + action.Name, action.Action, action.SupportsFilter));
            }

            return abilities;
        }
    }

    /// <summary>One ability the <c>permissions</c> extra answers.</summary>
    /// <param name="Name">The ability: <c>save</c>, <c>delete</c>, <c>activate</c> or <c>action:&lt;name&gt;</c>.</param>
    /// <param name="Action">The securable action it is granted under.</param>
    /// <param name="SupportsFilter">Whether a grant of it may carry a row filter; an action that does not is all-or-nothing.</param>
    internal sealed record RecordAbility(string Name, string Action, bool SupportsFilter);
}
