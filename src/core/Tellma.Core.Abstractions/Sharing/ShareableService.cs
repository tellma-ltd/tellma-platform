// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Sharing
{
    // Spec 0033 §5.1: the ownership rules every shareable stack shares. The owner is stamped here and
    // nowhere else but transfer: a new row takes the caller, or no one in a system scope, and an
    // update keeps its before image's owner whatever the payload says. A row with no owner is a
    // standard row, read-only outside a system scope except to share and publish. The grants live
    // beside the row in the share entity, which a save never reads or writes; a delete removes them
    // through the share entity's cascading foreign key.
    /// <summary>
    ///     The base of every shareable stack's service: stamps each new row's owner, keeps each saved
    ///     row's owner, keeps standard rows read-only outside a system scope, and checks that a row's
    ///     lineage names a row the caller can read.
    /// </summary>
    /// <remarks>
    ///     A derived service declares the <c>share</c>, <c>publish</c> and <c>transfer</c> actions and
    ///     delegates each to <see cref="Commands" />. Every hook it overrides calls the base first.
    /// </remarks>
    /// <typeparam name="TEntity">The shareable entity.</typeparam>
    /// <typeparam name="TShare">The share entity beside it.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    /// <param name="commands">The entity's sharing commands, which the derived service's actions delegate to.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public abstract class ShareableService<TEntity, TShare>(IEntityPipelineFactory factory, IShareCommands<TEntity> commands) : EntityService<TEntity, int>(factory)
        where TEntity : TopLevelEntity, IShareable
        where TShare : Entity<int>, IShareRow
    {
        /// <summary>The ability of a save, as the <c>permissions</c> extra names it.</summary>
        private const string SaveAbility = "save";

        /// <summary>The ability of a delete, as the <c>permissions</c> extra names it.</summary>
        private const string DeleteAbility = "delete";

        /// <summary>The ability of an activation in either direction, as the <c>permissions</c> extra names it.</summary>
        private const string ActivateAbility = "activate";

        /// <summary>The built-in action that activates rows.</summary>
        private const string ActivateAction = "activate";

        /// <summary>The built-in action that deactivates rows.</summary>
        private const string DeactivateAction = "deactivate";

        /// <summary>The prefix of an entity action's ability, as the <c>permissions</c> extra names it.</summary>
        private const string ActionAbilityPrefix = "action:";

        /// <summary>The property of a delete's or an action's targets, under which a target's error sits.</summary>
        private const string IdsProperty = "Ids";

        /// <summary>The most ids one lineage filter lists, under the query language's list ceiling.</summary>
        private const int IdsPerFilter = 500;

        /// <summary>The entity's sharing commands, which the <c>share</c>, <c>publish</c> and <c>transfer</c> actions delegate to.</summary>
        protected IShareCommands<TEntity> Commands { get; } = commands ?? throw new ArgumentNullException(nameof(commands));

        /// <summary>Stamps each new row's owner: the caller, or no one in a system scope.</summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rows are stamped.</returns>
        protected override async Task PreprocessAsync(SaveContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.PreprocessAsync(context).ConfigureAwait(false);

            // The owner is server-owned, so the payload's value was discarded before this hook: a new
            // row takes the caller here, and an update keeps its stored owner, which the pipeline
            // restores once the before images load.
            int? owner = context.Context.IsSystem ? null : context.Context.UserId;
            for (int index = 0; index < context.Entities.Count; index++)
            {
                if (context.IsNew(index))
                {
                    context.Entities[index].OwnerId = owner;
                }
            }
        }

        /// <summary>
        ///     Keeps each updated row's owner, refuses a standard row's update outside a system scope,
        ///     and refuses a new row based on a row the caller cannot read.
        /// </summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected override async Task ValidateAsync(SaveContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateAsync(context).ConfigureAwait(false);

            // --- The lineage of the new rows: one visible read beside the before images ---
            List<int> lineage = [.. Enumerable.Range(0, context.Entities.Count)
                .Where(context.IsNew)
                .Select(index => context.Entities[index].BasedOnId)
                .OfType<int>()
                .Distinct()
                .Order()];
            List<ContextRef<IReadOnlyList<TEntity>>> visible = [.. lineage.Chunk(IdsPerFilter)
                .Select(chunk => context.Loader.Visible<TEntity>(IdIn(chunk), select: nameof(Entity<>.Id)))];
            await context.Loader.LoadAsync().ConfigureAwait(false);

            HashSet<int> readable = [.. visible.SelectMany(static read => read.Value).Select(static row => row.Id)];
            for (int index = 0; index < context.Entities.Count; index++)
            {
                TEntity row = context.Entities[index];
                if (context.IsNew(index))
                {
                    if (row.BasedOnId is int basedOn && !readable.Contains(basedOn))
                    {
                        context.Errors.Add(index, static x => x.BasedOnId, SharingValidationCodes.BasedOnInvisible, Arguments("id", basedOn));
                    }

                    continue;
                }

                // --- A standard row is read-only outside a system scope ---
                if (context.Before(index) is { OwnerId: null } && !context.Context.IsSystem)
                {
                    context.Errors.Add(ValidationPath.Root.Index(index), SharingValidationCodes.StandardReadOnly, StandardArguments());
                }
            }
        }

        /// <summary>Refuses the delete of a standard row outside a system scope.</summary>
        /// <param name="context">The delete's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected override async Task ValidateDeleteAsync(DeleteContext<TEntity, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateDeleteAsync(context).ConfigureAwait(false);
            await context.Loader.LoadAsync().ConfigureAwait(false);
            RefuseStandard(context.Context.IsSystem, context.Ids, context.Entities, context.Errors);
        }

        /// <summary>Refuses the activation, the deactivation and the transfer of a standard row outside a system scope.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected override async Task ValidateActionAsync(string action, ActionContext<TEntity, int> context)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateActionAsync(action, context).ConfigureAwait(false);

            // Sharing and publishing a standard row is how an administrator hands a seed out.
            if (action is ActivateAction or DeactivateAction or ShareableNames.TransferAction)
            {
                await context.Loader.LoadAsync().ConfigureAwait(false);
                RefuseStandard(context.Context.IsSystem, context.Ids, context.Entities, context.Errors);
            }
        }

        /// <summary>Withdraws, in a delete's batch, every share notice of the deleted rows.</summary>
        /// <param name="context">The persist context, of the operation's kind.</param>
        /// <returns>A task that completes when the withdrawals are appended.</returns>
        protected override async Task ContributeAsync(PersistContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ContributeAsync(context).ConfigureAwait(false);

            // A delete removes every grant of its rows, so every notice those grants raised goes too.
            if (context is DeletePersistContext<TEntity> delete)
            {
                Commands.WithdrawAll(delete);
            }
        }

        /// <summary>A standard row refuses a save, a delete, an activation and a transfer; it can still be shared and published.</summary>
        /// <param name="row">The row.</param>
        /// <param name="ability">The ability.</param>
        /// <returns>The reason's resource key, or null.</returns>
        protected override string? RefuseOnRecord(TEntity row, string ability)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(ability);
            bool refused = row.OwnerId is null && ability is SaveAbility or DeleteAbility or ActivateAbility or ActionAbilityPrefix + ShareableNames.TransferAction;
            return refused ? SharingValidationCodes.StandardReadOnly : base.RefuseOnRecord(row, ability);
        }

        /// <summary>Adds the standard-row refusal at each standard target's position, outside a system scope.</summary>
        /// <param name="system">Whether the operation runs in a system scope, where standard rows are writable.</param>
        /// <param name="ids">The targets' ids as the caller sent them.</param>
        /// <param name="targets">The loaded targets.</param>
        /// <param name="errors">The operation's errors.</param>
        private void RefuseStandard(bool system, IReadOnlyList<int> ids, IReadOnlyList<TEntity> targets, ValidationErrors errors)
        {
            if (system)
            {
                return;
            }

            for (int position = 0; position < targets.Count; position++)
            {
                TEntity target = targets[position];
                if (target.OwnerId is null)
                {
                    int index = IndexOf(ids, target.Id);
                    errors.Add(ValidationPath.Root.Property(IdsProperty).Index(index < 0 ? position : index), SharingValidationCodes.StandardReadOnly, StandardArguments());
                }
            }
        }

        /// <summary>The arguments of the standard-row refusal: the entity's name.</summary>
        /// <returns>The arguments.</returns>
        private Dictionary<string, object?> StandardArguments()
        {
            return Arguments("entity", Descriptor.Resource);
        }

        /// <summary>The position of an id among the targets the caller sent; -1 when absent.</summary>
        /// <param name="ids">The ids.</param>
        /// <param name="id">The id.</param>
        /// <returns>The position.</returns>
        private static int IndexOf(IReadOnlyList<int> ids, int id)
        {
            for (int index = 0; index < ids.Count; index++)
            {
                if (ids[index] == id)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>The filter of the rows whose ids are among some ids, spelled as constants so the statement is plain.</summary>
        /// <param name="ids">The ids, at most the query language's list ceiling.</param>
        /// <returns>The filter.</returns>
        private static FilterTree IdIn(int[] ids)
        {
            StringBuilder text = new("Id in (");
            for (int index = 0; index < ids.Length; index++)
            {
                _ = text.Append(index == 0 ? string.Empty : ", ").Append(ids[index].ToString(CultureInfo.InvariantCulture));
            }

            return FilterTree.Leaf(text.Append(')').ToString());
        }

        /// <summary>One error argument.</summary>
        /// <param name="name">The argument's name.</param>
        /// <param name="value">Its value.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> Arguments(string name, object? value)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal) { [name] = value };
        }
    }
}
