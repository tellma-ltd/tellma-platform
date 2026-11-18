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
    /// <summary>The frame the pipeline opens around a save, a delete or an action: the host its participants see.</summary>
    /// <param name="services">The scope's write services.</param>
    /// <param name="stack">The stack.</param>
    /// <param name="operation">The <c>crud.operation</c> the write runs as, such as <c>save</c>.</param>
    /// <param name="confirmations">The operation's confirmation frame.</param>
    /// <param name="source">Who the write is recorded as coming from.</param>
    /// <param name="composeGuard">Refuses an enlistment from a flow that may compose no more, such as an action's task that read Persisted; null when none does.</param>
    internal sealed class PipelineFrame(
        SaveOperation services, StackDescriptor stack, string operation, IConfirmations confirmations, SaveSource source, Action? composeGuard = null)
        : EnlistmentFrame
    {
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
        public override string Operation { get; } = $"{stack.Resource}:{operation}";

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

            _rounds = new EnlistmentRounds(services, scheduler, loader, map, operation, services.Registry.ParticipantsOf(stack.Resource), cancellationToken);
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
}
