// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud.Writes
{
    // The delete and action pipeline suite's model: an activatable ticket whose service validates
    // deletes and actions and declares three entity actions (one answering the target ids, one
    // single-target answering a count of its own, one taking an arguments record), an activatable
    // label whose service validates nothing, so its deletes and its built-in actions persist on one
    // round trip, and a tree. Every hook and method reports to one shared body a test sets.

    /// <summary>Composes the delete and action pipeline suite's model.</summary>
    internal static class PipelineWriteModel
    {
        /// <summary>The tickets' resource, whose service validates deletes and actions.</summary>
        public const string Tickets = "pw.PwTicket";

        /// <summary>The labels' resource, whose service validates nothing.</summary>
        public const string Labels = "pw.PwLabel";

        /// <summary>The nodes' resource, a tree.</summary>
        public const string Nodes = "pw.PwNode";

        /// <summary>The ticket action that answers the target ids.</summary>
        public const string Close = "close";

        /// <summary>The single-target ticket action that answers a count of its own.</summary>
        public const string Count = "count";

        /// <summary>The ticket action that takes an arguments record.</summary>
        public const string Rename = "rename";

        /// <summary>Adds the model's feature.</summary>
        /// <param name="tellma">The composition's builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.B>("pipeline-write", contribute: static contribution => contribution
                .Entity<PwTicket, PwTicketService>()
                .Entity<PwLabel>()
                .Entity<PwNode>()));
        }
    }

    /// <summary>The model's ticket, activatable, whose service validates deletes and actions.</summary>
    [Table("Tickets", Schema = "pw")]
    [TableType]
    [Stack(MaxIds = 5, MaxDeleteByQueryRows = 10)]
    public class PwTicket : TopLevelEntity, IActivatable
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;

        /// <summary>The ticket's label, so a label still referenced is in use.</summary>
        [References(typeof(PwLabel))]
        public int? LabelId { get; set; }

        /// <summary>Whether the ticket is active.</summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>The model's label, activatable, whose service validates nothing.</summary>
    [Table("Labels", Schema = "pw")]
    [TableType]
    public class PwLabel : TopLevelEntity, IActivatable
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;

        /// <summary>Whether the label is active.</summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>The model's tree.</summary>
    [Table("Nodes", Schema = "pw")]
    [TableType]
    public class PwNode : TreeEntity
    {
        /// <summary>The code.</summary>
        [MaxLength(10)]
        public string Code { get; set; } = null!;
    }

    /// <summary>The arguments of the rename action.</summary>
    public sealed record PwRename
    {
        /// <summary>The new name, at most five characters.</summary>
        [Required]
        [MaxLength(5)]
        public string? Name { get; init; }

        /// <summary>The priority, from 1 to 9.</summary>
        [Range(1, 9)]
        public int Priority { get; init; } = 1;
    }

    /// <summary>What the tickets' hooks and methods do, which a test sets: shared by the rig's scopes.</summary>
    public sealed class PwHooks
    {
        /// <summary>The delete validation hook; nothing when null.</summary>
        public Func<DeleteContext<PwTicket, int>, Task>? ValidateDelete { get; set; }

        /// <summary>The action validation hook, given the action's name; nothing when null.</summary>
        public Func<string, ActionContext<PwTicket, int>, Task>? ValidateAction { get; set; }

        /// <summary>The close action's body; nothing when null.</summary>
        public Func<ActionContext<PwTicket, int>, Task>? Close { get; set; }

        /// <summary>The count action's body; the target count when null.</summary>
        public Func<ActionContext<PwTicket, int>, Task<int>>? Count { get; set; }

        /// <summary>The rename action's body; nothing when null.</summary>
        public Func<ActionContext<PwTicket, int>, PwRename, Task>? Rename { get; set; }

        /// <summary>The persist hook; nothing when null.</summary>
        public Func<PersistContext<PwTicket>, Task>? Contribute { get; set; }

        /// <summary>What each committed operation's post-commit phase saw, in order.</summary>
        public List<PersistOutcome<PwTicket>> Outcomes { get; } = [];
    }

    /// <summary>The tickets' service: its validation hooks, persist hooks and actions run the shared body.</summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="hooks">The shared body.</param>
    public sealed class PwTicketService(IEntityPipelineFactory factory, PwHooks hooks) : EntityService<PwTicket>(factory)
    {
        /// <summary>Closes the tickets.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the action composed.</returns>
        [EntityAction(PipelineWriteModel.Close)]
        public Task CloseAsync(ActionContext<PwTicket, int> context)
        {
            return hooks.Close?.Invoke(context) ?? Task.CompletedTask;
        }

        /// <summary>Counts something of one ticket.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The count.</returns>
        [EntityAction(PipelineWriteModel.Count, SingleTarget = true, Mutation = false)]
        public Task<int> CountAsync(ActionContext<PwTicket, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return hooks.Count?.Invoke(context) ?? Task.FromResult(context.Ids.Count);
        }

        /// <summary>Renames the tickets.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The new name.</param>
        /// <returns>A task that completes when the action composed.</returns>
        [EntityAction(PipelineWriteModel.Rename)]
        public Task RenameAsync(ActionContext<PwTicket, int> context, PwRename arguments)
        {
            return hooks.Rename?.Invoke(context, arguments) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ValidateDeleteAsync(DeleteContext<PwTicket, int> context)
        {
            return hooks.ValidateDelete?.Invoke(context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ValidateActionAsync(string action, ActionContext<PwTicket, int> context)
        {
            return hooks.ValidateAction?.Invoke(action, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ContributeAsync(PersistContext<PwTicket> context)
        {
            return hooks.Contribute?.Invoke(context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task AfterCommitAsync(PersistOutcome<PwTicket> outcome)
        {
            hooks.Outcomes.Add(outcome);
            return Task.CompletedTask;
        }
    }

    /// <summary>A label validator that validates saves alone, keeping the delete and action hooks' defaults.</summary>
    public sealed class PwSaveOnlyValidator : IEntityValidator<PwLabel>
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<PwLabel> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A label validator that validates deletes.</summary>
    public sealed class PwDeleteValidator : IEntityValidator<PwLabel>
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<PwLabel> context)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ValidateDeleteAsync(DeleteContext<PwLabel> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A label validator that validates actions.</summary>
    public sealed class PwActionValidator : IEntityValidator<PwLabel>
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<PwLabel> context)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ValidateActionAsync(string action, ActionContext<PwLabel> context)
        {
            return Task.CompletedTask;
        }
    }
}
