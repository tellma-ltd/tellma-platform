// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Module.Gl
{
    // Spec 0018 §5.4. The parents the payload does not carry load in the round the pipeline's own
    // round trip 1 carries, so a save stays at two round trips and a delete at two. Two refinements
    // of the spec's rules, both exact with no further load: a type change is refused only when the
    // center would keep a child, so a save that moves every child elsewhere is accepted; and a
    // delete is refused only when it leaves a child behind, so a delete that names a center's whole
    // subtree is accepted, as the self-referencing foreign key accepts it.
    /// <summary>
    ///     The responsibility centers' stack service: the standard operations over the tenant's center
    ///     tree, closed over the distribution's center leaf, with the rules that keep the tree's shape.
    /// </summary>
    /// <remarks>
    ///     <para>A save is refused, with <see cref="GlValidationCodes.ParentMustBeGrouping" />:</para>
    ///     <list type="bullet">
    ///         <item>
    ///             at a row's parent, when the parent (as the payload saves it, else as stored) is not an
    ///             abstract center or a business unit;
    ///         </item>
    ///         <item>
    ///             at a stored row's type, when the save changes it to a type that groups nothing while
    ///             the row keeps a child: a stored child the payload does not move elsewhere, or a
    ///             payload row naming it as parent.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         A row that names itself as parent is the platform's tree cycle, and a parent no visible
    ///         row has is the platform's not-found reference; neither is judged again here, so a hidden
    ///         parent's type never shows through an error.
    ///     </para>
    ///     <para>
    ///         A delete by ids is refused with <see cref="GlValidationCodes.HasChildren" /> at a target
    ///         that keeps a child the delete does not name; a delete with descendants is never refused
    ///         here. A leaf that overrides a hook calls this one to keep its rules.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TCenter">The stack's center leaf.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    public class CenterService<TCenter>(IEntityPipelineFactory factory) : EntityService<TCenter>(factory)
        where TCenter : Center
    {
        /// <summary>What a parent outside the payload loads: its type alone.</summary>
        internal const string ParentSelect = "Id, CenterType";

        /// <summary>The root property a delete's target ids are reported under.</summary>
        private const string IdsProperty = "Ids";

        /// <summary>Validates a save: every row's parent groups, and no row stops grouping while it keeps a child.</summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected override async Task ValidateAsync(SaveContext<TCenter> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateAsync(context).ConfigureAwait(false);
            IReadOnlyList<TCenter> rows = context.Entities;

            // --- The payload by id (ids are final here), and the parents it does not carry ---
            Dictionary<int, int> payload = [];
            for (int index = 0; index < rows.Count; index++)
            {
                _ = payload.TryAdd(rows[index].Id, index);
            }

            int[] outside = [.. rows
                .Where(row => row.ParentId is int parentId && parentId > 0 && parentId != row.Id && !payload.ContainsKey(parentId))
                .Select(static row => row.ParentId!.Value)
                .Distinct()
                .Order()];

            // --- One round: the outside parents ride the pipeline's round trip 1 with the before images ---
            ContextRef<IReadOnlyDictionary<int, TCenter>>? loads = outside.Length == 0 ? null : context.Loader.ByIds<TCenter, int>(outside, ParentSelect);
            bool updates = Enumerable.Range(0, rows.Count).Any(index => !context.IsNew(index));
            if (loads is null && !updates)
            {
                // Every row is new and every parent is in the payload: nothing to wait for.
                CheckParents(context, payload, loaded: null, refused: []);
                return;
            }

            await context.Loader.LoadAsync().ConfigureAwait(false);

            // The pipeline's own checks ran before this validator resumed: a parent it already
            // refused (not found, or hidden from the caller) is not judged again.
            HashSet<ValidationPath> refused = [.. context.Errors.Items.Select(static error => error.Path)];
            CheckParents(context, payload, loads?.Value, refused);
            CheckTypeChanges(context);
        }

        /// <summary>Validates a delete by ids: no target keeps a child the delete does not name.</summary>
        /// <param name="context">The delete's context.</param>
        /// <returns>A task that completes when the rule ran.</returns>
        protected override async Task ValidateDeleteAsync(DeleteContext<TCenter, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateDeleteAsync(context).ConfigureAwait(false);

            // A delete with descendants removes every child with its root.
            if (context.WithDescendants)
            {
                return;
            }

            // --- The targets load with round trip 1, every mapped property included ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            IReadOnlyList<TCenter> targets = context.Entities;
            Dictionary<int, int> named = TargetedDescendants(targets);
            for (int position = 0; position < targets.Count; position++)
            {
                TCenter target = targets[position];
                if (target.SubtreeCount - 1 > named.GetValueOrDefault(target.Id))
                {
                    context.Errors.Add(TargetPath(context.Ids, target.Id, position), GlValidationCodes.HasChildren);
                }
            }
        }

        /// <summary>
        ///     The number of each target's descendants the delete names: those whose chain of parents up
        ///     to the target runs through targets alone.
        /// </summary>
        /// <param name="targets">The targets, as stored.</param>
        /// <returns>The count, by target id; a target with none is absent.</returns>
        internal static Dictionary<int, int> TargetedDescendants(IReadOnlyList<TCenter> targets)
        {
            Dictionary<int, TCenter> byId = [];
            foreach (TCenter target in targets)
            {
                _ = byId.TryAdd(target.Id, target);
            }

            // --- Each target counts once for every targeted ancestor its chain reaches ---
            // The walk is bounded by the number of targets, so stored data that is not a tree never
            // loops; a whole subtree named is exactly its root's subtree count less one.
            Dictionary<int, int> counts = [];
            foreach (TCenter target in byId.Values)
            {
                int? parentId = target.ParentId;
                for (int steps = 0; steps < byId.Count && parentId is int id && id != target.Id && byId.TryGetValue(id, out TCenter? parent); steps++)
                {
                    counts[id] = counts.GetValueOrDefault(id) + 1;
                    parentId = parent.ParentId;
                }
            }

            return counts;
        }

        /// <summary>The rule over each row's parent: it groups, as the payload saves it or as stored.</summary>
        /// <param name="context">The save's context.</param>
        /// <param name="payload">The payload's rows by id.</param>
        /// <param name="loaded">The parents outside the payload, by id; null when none loaded.</param>
        /// <param name="refused">The paths the pipeline already reported.</param>
        private static void CheckParents(SaveContext<TCenter> context, Dictionary<int, int> payload, IReadOnlyDictionary<int, TCenter>? loaded, HashSet<ValidationPath> refused)
        {
            IReadOnlyList<TCenter> rows = context.Entities;
            for (int index = 0; index < rows.Count; index++)
            {
                TCenter row = rows[index];

                // A root has no parent to judge, and a row under itself is the platform's tree cycle.
                if (row.ParentId is not int parentId || parentId == row.Id)
                {
                    continue;
                }

                // --- The parent's type: the payload's, else the stored one's ---
                CenterType? parentType;
                if (payload.TryGetValue(parentId, out int parentIndex))
                {
                    parentType = rows[parentIndex].CenterType;
                }
                else if (context.IsNew(index) || context.Changed(index, static center => center.ParentId))
                {
                    parentType = loaded is not null && loaded.TryGetValue(parentId, out TCenter? parent) ? parent.CenterType : null;
                }
                else
                {
                    // An unchanged parent the payload leaves alone was judged when it was saved, and
                    // a grouping center stops grouping only once it has no children.
                    continue;
                }

                ValidationPath at = ValidationPath.Root.Index(index).Property(nameof(Center.ParentId));
                if (parentType is CenterType type && !CenterTypes.IsGrouping(type) && !refused.Contains(at))
                {
                    context.Errors.Add(at, GlValidationCodes.ParentMustBeGrouping);
                }
            }
        }

        /// <summary>The rule over each stored row whose type stops grouping: it keeps no child.</summary>
        /// <param name="context">The save's context, after its first round.</param>
        private static void CheckTypeChanges(SaveContext<TCenter> context)
        {
            IReadOnlyList<TCenter> rows = context.Entities;
            for (int index = 0; index < rows.Count; index++)
            {
                TCenter row = rows[index];
                if (context.IsNew(index) || CenterTypes.IsGrouping(row.CenterType) || !context.Changed(index, static center => center.CenterType))
                {
                    continue;
                }

                if (KeepsChild(context, index))
                {
                    context.Errors.Add(index, static center => center.CenterType, GlValidationCodes.ParentMustBeGrouping);
                }
            }
        }

        /// <summary>
        ///     Whether a stored row keeps a child after the save: another payload row names it as
        ///     parent, or the stored rows under it outnumber the subtrees the payload moves elsewhere.
        /// </summary>
        /// <param name="context">The save's context, after its first round.</param>
        /// <param name="index">The stored row.</param>
        /// <returns>True when the row would keep a child.</returns>
        private static bool KeepsChild(SaveContext<TCenter> context, int index)
        {
            IReadOnlyList<TCenter> rows = context.Entities;
            int id = rows[index].Id;
            int descendants = (context.Before(index)?.SubtreeCount ?? 1) - 1;
            int movedAway = 0;
            for (int other = 0; other < rows.Count; other++)
            {
                if (other == index)
                {
                    continue;
                }

                // --- A payload row under the center, new or kept there, is a child it keeps ---
                if (rows[other].ParentId == id)
                {
                    return true;
                }

                // --- A stored child the payload moves elsewhere takes its whole subtree along ---
                if (!context.IsNew(other) && context.Before(other) is TCenter before && before.ParentId == id)
                {
                    movedAway += before.SubtreeCount;
                }
            }

            return descendants > movedAway;
        }

        /// <summary>The path of a delete's target: <c>Ids[i]</c>, at the first position the request names the id.</summary>
        /// <param name="ids">The ids as the request named them.</param>
        /// <param name="id">The target's id.</param>
        /// <param name="position">The target's position among the loaded targets, the fallback.</param>
        /// <returns>The path.</returns>
        private static ValidationPath TargetPath(IReadOnlyList<int> ids, int id, int position)
        {
            int index = -1;
            for (int candidate = 0; candidate < ids.Count; candidate++)
            {
                if (ids[candidate] == id)
                {
                    index = candidate;
                    break;
                }
            }

            return ValidationPath.Root.Property(IdsProperty).Index(index < 0 ? position : index);
        }
    }
}
