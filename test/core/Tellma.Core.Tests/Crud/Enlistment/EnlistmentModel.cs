// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud.Enlistment
{
    // The enlistment suite's model: an order whose service enlists notes and its own orders and
    // declares two API actions (one under the order's Save, one for any member), a note whose
    // service enlists tags, a tag, and an other stack whose service also enlists notes, so a writer
    // can declare the pair yet take no part in an order's frame. A job handler enlists notes from
    // its own frame, and a provisioning step from its. Every hook and method runs what one shared
    // body sets.

    /// <summary>Composes the enlistment suite's model.</summary>
    internal static class EnlistmentModel
    {
        /// <summary>The orders' resource: the host of most frames.</summary>
        public const string Orders = "en.EnOrder";

        /// <summary>The notes' resource: the orders' target.</summary>
        public const string Notes = "en.EnNote";

        /// <summary>The tags' resource: the notes' target.</summary>
        public const string Tags = "en.EnTag";

        /// <summary>The other stack's resource, whose service declares the notes' pair too.</summary>
        public const string Others = "en.EnOther";

        /// <summary>The order action that runs under the order's Save.</summary>
        public const string Stamp = "stamp";

        /// <summary>The order action any member may call.</summary>
        public const string Ping = "ping";

        /// <summary>The job handler's key.</summary>
        public const string SweepKey = "en.sweep";

        /// <summary>The provisioning step's name.</summary>
        public const string SeedStep = "en.seed";

        /// <summary>Adds the model's feature.</summary>
        /// <param name="tellma">The composition's builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.C>("enlistment", contribute: static contribution => contribution
                .Entity<EnOrder, EnOrderService>()
                .Entity<EnNote, EnNoteService>()
                .Entity<EnTag>()
                .Entity<EnOther, EnOtherService>()
                .JobHandler<EnSweepJob>()
                .ProvisioningStep<EnSeedStep>()));
        }
    }

    /// <summary>The model's order, which enlists notes and its own rows.</summary>
    [Table("Orders", Schema = "en")]
    [TableType]
    [Stack(Operations = StackOperations.All | StackOperations.Enlist)]
    public class EnOrder : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;
    }

    /// <summary>The model's note, which accepts enlisted writes and enlists tags.</summary>
    [Table("Notes", Schema = "en")]
    [TableType]
    [Stack(Operations = StackOperations.All | StackOperations.Enlist)]
    public class EnNote : TopLevelEntity
    {
        /// <summary>The text, required.</summary>
        [Required]
        [MaxLength(20)]
        public string? Text { get; set; }
    }

    /// <summary>The model's tag, which accepts enlisted writes.</summary>
    [Table("Tags", Schema = "en")]
    [TableType]
    [Stack(Operations = StackOperations.All | StackOperations.Enlist)]
    public class EnTag : TopLevelEntity
    {
        /// <summary>The label, required.</summary>
        [Required]
        [MaxLength(20)]
        public string? Label { get; set; }
    }

    /// <summary>The model's other entity, whose service declares the notes' pair.</summary>
    [Table("Others", Schema = "en")]
    [TableType]
    public class EnOther : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;
    }

    /// <summary>The body of the order's stamp action.</summary>
    public sealed record EnStamp
    {
        /// <summary>The name to stamp, at most five characters.</summary>
        [Required]
        [MaxLength(5)]
        public string? Name { get; init; }
    }

    /// <summary>What the model's hooks and methods do, which a test sets: shared by the rig's scopes.</summary>
    public sealed class EnHooks
    {
        /// <summary>The order preprocessing hook; nothing when null.</summary>
        public Func<EnOrderService, SaveContext<EnOrder>, Task>? OrderPreprocess { get; set; }

        /// <summary>The order validation hook; nothing when null.</summary>
        public Func<EnOrderService, SaveContext<EnOrder>, Task>? OrderValidate { get; set; }

        /// <summary>The order persist hook; nothing when null.</summary>
        public Func<EnOrderService, PersistContext<EnOrder>, Task>? OrderContribute { get; set; }

        /// <summary>The note validation hook; nothing when null.</summary>
        public Func<EnNoteService, SaveContext<EnNote>, Task>? NoteValidate { get; set; }

        /// <summary>The note delete validation hook; nothing when null.</summary>
        public Func<EnNoteService, DeleteContext<EnNote, int>, Task>? NoteValidateDelete { get; set; }

        /// <summary>The note persist hook; nothing when null.</summary>
        public Func<EnNoteService, PersistContext<EnNote>, Task>? NoteContribute { get; set; }

        /// <summary>The stamp action's body, given the injected open frame; the body's name when null.</summary>
        public Func<EnOrderService, IOpenWriteHost, EnStamp, CancellationToken, Task<string>>? Stamp { get; set; }

        /// <summary>The ping action's body, given the injected open frame; zero when null.</summary>
        public Func<EnOrderService, IOpenWriteHost, Task<int>>? Ping { get; set; }

        /// <summary>The job handler's body, given the injected open frame; nothing when null.</summary>
        public Func<EnSweepJob, IOpenWriteHost, Task>? Sweep { get; set; }

        /// <summary>The provisioning step's body, given the injected open frame; nothing when null.</summary>
        public Func<EnSeedStep, IOpenWriteHost, Task>? Seed { get; set; }

        /// <summary>The resource of each stack whose post-commit phase ran, in order.</summary>
        public List<string> Committed { get; } = [];

        /// <summary>The order the interceptors and the action methods ran in, by name.</summary>
        public List<string> Calls { get; } = [];
    }

    /// <summary>The orders' service: its hooks and its actions run the shared body.</summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="hooks">The shared body.</param>
    /// <param name="host">The open frame of the action that runs.</param>
    public sealed class EnOrderService(IEntityPipelineFactory factory, EnHooks hooks, IOpenWriteHost host)
        : EntityService<EnOrder>(factory), IEnlists<EnNote>, IEnlists<EnOrder>
    {
        /// <summary>Stamps something under the order's Save.</summary>
        /// <param name="body">The body.</param>
        /// <param name="cancellationToken">The invocation's token.</param>
        /// <returns>What the body answered.</returns>
        [ApiAction(EnlistmentModel.Stamp, Action = "Save")]
        public Task<string> StampAsync(EnStamp body, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(body);
            hooks.Calls.Add(EnlistmentModel.Stamp);
            return hooks.Stamp?.Invoke(this, host, body, cancellationToken) ?? Task.FromResult(body.Name!);
        }

        /// <summary>Answers any member.</summary>
        /// <returns>What the body answered.</returns>
        [ApiAction(EnlistmentModel.Ping, MemberOnly = true, Mutation = false)]
        public Task<int> PingAsync()
        {
            hooks.Calls.Add(EnlistmentModel.Ping);
            return hooks.Ping?.Invoke(this, host) ?? Task.FromResult(0);
        }

        /// <inheritdoc />
        protected override Task PreprocessAsync(SaveContext<EnOrder> context)
        {
            return hooks.OrderPreprocess?.Invoke(this, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ValidateAsync(SaveContext<EnOrder> context)
        {
            return hooks.OrderValidate?.Invoke(this, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ContributeAsync(PersistContext<EnOrder> context)
        {
            return hooks.OrderContribute?.Invoke(this, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task AfterCommitAsync(PersistOutcome<EnOrder> outcome)
        {
            hooks.Committed.Add(EnlistmentModel.Orders);
            return Task.CompletedTask;
        }
    }

    /// <summary>The notes' service: its hooks run the shared body.</summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="hooks">The shared body.</param>
    public sealed class EnNoteService(IEntityPipelineFactory factory, EnHooks hooks) : EntityService<EnNote>(factory), IEnlists<EnTag>
    {
        /// <inheritdoc />
        protected override Task ValidateAsync(SaveContext<EnNote> context)
        {
            return hooks.NoteValidate?.Invoke(this, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ValidateDeleteAsync(DeleteContext<EnNote, int> context)
        {
            return hooks.NoteValidateDelete?.Invoke(this, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ContributeAsync(PersistContext<EnNote> context)
        {
            return hooks.NoteContribute?.Invoke(this, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task AfterCommitAsync(PersistOutcome<EnNote> outcome)
        {
            hooks.Committed.Add(EnlistmentModel.Notes);
            return Task.CompletedTask;
        }
    }

    /// <summary>The other stack's service, which declares the notes' pair but takes no part in an order's frame.</summary>
    /// <param name="factory">The pipeline factory.</param>
    public sealed class EnOtherService(IEntityPipelineFactory factory) : EntityService<EnOther>(factory), IEnlists<EnNote>;

    /// <summary>A class the composition never registered, which declares the notes' pair in its type alone.</summary>
    public sealed class EnOutsider : IEnlists<EnNote>;

    /// <summary>A job handler that enlists notes from its own frame.</summary>
    /// <param name="hooks">The shared body.</param>
    /// <param name="host">The handler's open frame.</param>
    [JobHandler(EnlistmentModel.SweepKey)]
    public sealed class EnSweepJob(EnHooks hooks, IOpenWriteHost host) : IJobHandler, IEnlists<EnNote>
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return hooks.Sweep?.Invoke(this, host) ?? Task.CompletedTask;
        }
    }

    /// <summary>A provisioning step that enlists notes from its own frame.</summary>
    /// <param name="hooks">The shared body.</param>
    /// <param name="host">The step's open frame.</param>
    public sealed class EnSeedStep(EnHooks hooks, IOpenWriteHost host) : ITenantProvisioningStep, IEnlists<EnNote>
    {
        /// <inheritdoc />
        public string Name => EnlistmentModel.SeedStep;

        /// <inheritdoc />
        public int Order => 150;

        /// <inheritdoc />
        public int Version => 1;

        /// <inheritdoc />
        public Task RunAsync(TenantProvisioningContext context)
        {
            return hooks.Seed?.Invoke(this, host) ?? Task.CompletedTask;
        }
    }
}
