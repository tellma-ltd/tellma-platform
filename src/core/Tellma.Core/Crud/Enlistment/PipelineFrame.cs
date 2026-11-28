// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §13.3: a pipeline frame is composing from its id assignment to the close of its last
    // validation round, and admits enlistment only then: from its validation hooks, an entity
    // action's method and its validator components, never from preprocessing, a persist effect or
    // post-commit work. Run inside an open frame, it holds that frame's persist back while it composes.
    // Each group is recorded with the task that enlisted it (§5.1): a re-run of the validators is
    // answered from the group the same task enlisted at the same step, never from another task's.
    // The frame's Operation names an action host by its action, as "<Resource>:<name>" (§13.3); its
    // enlistments are counted under the host's §16.1 operation, every delete's as delete.
    /// <summary>The frame the pipeline opens around a save, a delete or an action: the host its participants see.</summary>
    /// <param name="services">The scope's write services.</param>
    /// <param name="stack">The stack.</param>
    /// <param name="operation">The <c>crud.operation</c> the write runs as, such as <c>save</c>.</param>
    /// <param name="confirmations">The operation's confirmation frame.</param>
    /// <param name="source">Who the write is recorded as coming from.</param>
    /// <param name="composeGuard">Refuses an enlistment from a flow that may compose no more, such as an action's task that read Persisted; null when none does.</param>
    /// <param name="action">The entity action the write runs, which names the frame's operation; null for a save or a delete.</param>
    internal sealed class PipelineFrame(
        SaveOperation services,
        StackDescriptor stack,
        string operation,
        IConfirmations confirmations,
        SaveSource source,
        Action? composeGuard = null,
        string? action = null)
        : EnlistmentFrame
    {
        /// <summary>Guards the records and the slots' counts.</summary>
        private readonly Lock _recordsGate = new();

        /// <summary>Every group enlisted with the frame directly, with the task that enlisted it, in enlistment order.</summary>
        private readonly List<EnlistmentRecord> _records = [];

        /// <summary>The enlistments each of the operation's own tasks made so far, by its slot.</summary>
        private readonly Dictionary<int, int> _steps = [];

        /// <summary>The frame's phase, read across flows.</summary>
        private volatile FramePhase _phase;

        /// <summary>The rounds, once the operation started them.</summary>
        private EnlistmentRounds? _rounds;

        /// <summary>The operation's errors, once its rounds started.</summary>
        private ValidationErrors? _errors;

        /// <summary>The open frame the operation runs in, which waits for its rounds to close; null when it runs in none.</summary>
        private OpenWriteFrame? _open;

        /// <summary>What the frame is doing.</summary>
        private enum FramePhase
        {
            /// <summary>Before its id assignment, or before its rounds started.</summary>
            Preparing,

            /// <summary>From its id assignment to the close of its last round.</summary>
            Composing,

            /// <summary>Its rounds closed: persisting, committed, or failed.</summary>
            Closed,
        }

        /// <inheritdoc />
        public override WriteHostKind Kind => WriteHostKind.Pipeline;

        /// <inheritdoc />
        public override string Operation { get; } = $"{stack.Resource}:{action ?? operation}";

        /// <inheritdoc />
        public override IConfirmations Confirmations { get; } = confirmations;

        /// <inheritdoc />
        public override EnlistmentRounds Rounds => _rounds ?? throw new InvalidOperationException(
            $"The frame of {Operation} admits enlistment once its validation rounds started.");

        /// <inheritdoc />
        public override ValidationErrors Errors => _errors ?? throw new InvalidOperationException(
            $"The frame of {Operation} collects errors once its validation rounds started.");

        /// <inheritdoc />
        public override SaveSource Source { get; } = source;

        /// <inheritdoc />
        public override bool IsComposing => _phase == FramePhase.Composing;

        /// <summary>Whether the frame's rounds started, so groups may have enlisted with it.</summary>
        public bool HasRounds => _rounds is not null;

        /// <summary>Every group enlisted with the frame directly, with the task that enlisted it, in enlistment order.</summary>
        public IReadOnlyList<EnlistmentRecord> Records
        {
            get
            {
                lock (_recordsGate)
                {
                    return [.. _records];
                }
            }
        }

        /// <summary>
        ///     Starts composing: the operation's rounds are those its groups' stages run in, and its
        ///     errors those theirs re-home into. An open frame around the operation waits to persist
        ///     until the frame closed.
        /// </summary>
        /// <param name="scheduler">The operation's scheduler.</param>
        /// <param name="loader">The operation's loader.</param>
        /// <param name="map">The tenant's schema map.</param>
        /// <param name="errors">The operation's errors.</param>
        /// <param name="cancellationToken">Cancels the operation's rounds.</param>
        public void BeginComposing(ValidationScheduler scheduler, ContextLoader loader, QueryexSchemaMap map, ValidationErrors errors, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(scheduler);
            ArgumentNullException.ThrowIfNull(loader);
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(errors);
            if (_phase != FramePhase.Preparing)
            {
                throw new InvalidOperationException($"The frame of {Operation} composes once.");
            }

            _rounds = new EnlistmentRounds(services, scheduler, loader, map, CountedAs(operation), services.Registry.ParticipantsOf(stack.Resource), cancellationToken);
            _errors = errors;
            _open = FrameFlow.InnermostOpen;
            _open?.NestedComposing(entering: true);
            _phase = FramePhase.Composing;
        }

        /// <summary>
        ///     Closes the rounds, after which nothing more enlists. Closing twice, or a frame that never
        ///     composed, changes nothing.
        /// </summary>
        /// <param name="complete">
        ///     Whether the rounds ended well, so what follows them runs for every group, such as the
        ///     value sets; false on the way out of a failed operation.
        /// </param>
        public void Close(bool complete = false)
        {
            if (_phase == FramePhase.Closed)
            {
                return;
            }

            bool composing = _phase == FramePhase.Composing;
            _phase = FramePhase.Closed;
            if (composing)
            {
                _rounds!.Close(complete);
                _open?.NestedComposing(entering: false);
            }
        }

        /// <summary>The <c>crud.operation</c> a pipeline host's enlistments are counted under: a delete of any kind as <c>delete</c>, any other as it runs.</summary>
        /// <param name="operation">The <c>crud.operation</c> the write runs as.</param>
        /// <returns>The value the enlistments counter records.</returns>
        public static string CountedAs(string operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            return operation is CrudOperationNames.DeleteByQuery or CrudOperationNames.DeleteWithDescendants ? CrudOperationNames.Delete : operation;
        }

        // Spec 0015 §5.1: the task is the operation's own task the writer's flow runs as, by its
        // slot, and the step its count of enlistments so far. A step taken while another flow of the
        // task is parked at a load races that flow's resumption; a re-run, whose loads are answered at
        // once, may take the two in the other order, so the record is marked.
        /// <inheritdoc />
        protected override void OnEnlisted(IEnlistedGroup group)
        {
            ValidationScheduler scheduler = Rounds.Scheduler;
            int? slot = scheduler.CurrentSlot;
            bool raced = scheduler.IsCurrentTaskParked;
            lock (_recordsGate)
            {
                int step = -1;
                if (slot is int task)
                {
                    step = _steps.GetValueOrDefault(task);
                    _steps[task] = step + 1;
                }

                _records.Add(new EnlistmentRecord(group, slot, step, raced));
            }
        }

        /// <inheritdoc />
        protected override void EnsureAdmits(Type writer)
        {
            if (_phase != FramePhase.Composing)
            {
                throw new InvalidOperationException(
                    $"The class {writer.Name} enlisted a write with {Operation} while it was not validating. A pipeline frame admits enlistment from its validation hooks, an entity action's method and its validator components, never from preprocessing, a persist effect or post-commit work.");
            }

            composeGuard?.Invoke();
        }
    }

    /// <summary>A group enlisted with a pipeline frame, with the task that enlisted it.</summary>
    /// <param name="Group">The group.</param>
    /// <param name="Slot">The slot of the operation's own task that enlisted it; null for a flow that runs as none, such as an action's method, which a re-run never repeats.</param>
    /// <param name="Step">The task's count of enlistments before this one; -1 when <paramref name="Slot" /> is null.</param>
    /// <param name="Raced">Whether another flow of the task was parked at a load when it enlisted, so a re-run may enlist it at another step.</param>
    internal sealed record EnlistmentRecord(IEnlistedGroup Group, int? Slot, int Step, bool Raced);
}
