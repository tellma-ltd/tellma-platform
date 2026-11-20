// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Crud.Actions
{
    // Spec 0015 §11.2 steps 2 and 3: round trip 1 carries the targets under the caller's Read filter,
    // with every child collection when the action loads children, and their count under the action's
    // grant, beside every load the hook, the components and the method request before their first
    // await. The three run as tasks of the validation rounds over one ActionContext. A task that must
    // know every validator's verdict before it acts, such as a method calling an identity server
    // between the round trips (spec 0018 §3.3), awaits the context's ValidatedAsync: the scheduler
    // resumes it at the quiet point that would end the rounds, at no round trip. The method
    // composes before the persist batch exists: its Batch is a deferred recording replayed onto the
    // persist, and a task that reads Persisted leaves the rounds once the flow that read it stops
    // running on its thread, so the persist it waits for runs once every other task completed or
    // parked (§11.2, reading its own writes). From the read on, the task composes nothing more: a
    // load, a save, an update or an append is refused, and a refusal raised before the flow stops
    // running faults it before the task leaves, so the rounds fail with it and the action is
    // abandoned unsent (§11.2: the method's exception abandons the batch). A plain Task tells no
    // await of Persisted from an await of anything else, so a flow that read Persisted and awaited
    // something else may compose after the persist was sent: every refusal is recorded on the run,
    // whichever flow raised it; one recorded before the persist is sent abandons the action, and
    // one recorded after it is raised once the action committed, for every action, never answered
    // as success. A method that throws PartialFailureException leaves what it composed to be
    // persisted, and the exception to be rethrown once the action committed. The tasks run inside
    // the action's pipeline frame, which admits enlistment while the rounds run (§13.3), from every
    // flow but one whose task read Persisted.
    /// <summary>One action of one stack: the general path's context, its tasks and their rounds.</summary>
    internal sealed partial class ActionRun<TEntity, TKey>
    {
        /// <summary>
        ///     The gate of the task the current flow runs in. A flow of a task that read
        ///     <see cref="ActionContext{TEntity}.Persisted" /> leaves the gate's context when it stops running
        ///     on its thread, and the task leaves the rounds there.
        /// </summary>
        private static readonly AsyncLocal<PersistGate?> Waiting = new(static change =>
        {
            // The thread left the reading flow's context: as a rule because the flow suspended or
            // returned, though also when the flow synchronously completes a source whose continuation
            // runs inline under another task's context. "Stopped running" means the thread left the
            // flow's context; a refusal the flow raises after that is recorded, never lost.
            if (change.ThreadContextChanged && change.PreviousValue is { IsLeaving: true } gate && !ReferenceEquals(change.CurrentValue, gate))
            {
                gate.Left();
            }
        });

        /// <summary>The flows that left the rounds to wait for the persist, whose failure before it is sent abandons the action.</summary>
        private readonly ConcurrentQueue<ReleasedFlow> _released = new();

        /// <summary>The first refusal of a write composed after its task read Persisted, whichever flow raised it; null when none was.</summary>
        private Exception? _composedLate;

        /// <summary>The first run's scheduler, which records a load refused after its task left the rounds; null on the one-round-trip path.</summary>
        private ValidationScheduler? _scheduler;

        /// <summary>The operation's loader; null on the one-round-trip path.</summary>
        private ContextLoader? _loader;

        /// <summary>The context the hook, the components and the method see; null on the one-round-trip path.</summary>
        private ActionContext<TEntity, TKey>? _context;

        /// <summary>The recording of the appends the hook, the components and the method composed onto the context's batch.</summary>
        private RecordingBatch? _recording;

        /// <summary>The visible targets round trip 1 loaded, and the action grant's count over them.</summary>
        private (IReadOnlyList<TEntity> Rows, int? Granted) _loaded = ([], null);

        /// <summary>The targets, in request order, once round trip 1 settled them; empty on the one-round-trip path.</summary>
        private IReadOnlyList<TEntity> _targets = [];

        /// <summary>The method's task; null for a built-in action.</summary>
        private Task? _methodTask;

        /// <summary>What the method returned; null for a method without a result.</summary>
        private object? _result;

        /// <summary>The partial failure the method threw after composing, rethrown once the action committed.</summary>
        private ExceptionDispatchInfo? _partial;

        /// <summary>The frame the first run's tasks run in; null on the one-round-trip path.</summary>
        private PipelineFrame? _frame;

        /// <summary>The rounds the groups enlisted with the action run in; null while none can have enlisted.</summary>
        private EnlistmentRounds? Enlistments => _frame is { HasRounds: true } frame ? frame.Rounds : null;

        /// <summary>Runs the general path: round trip 1, the validators and the method with their rounds, then the persist.</summary>
        /// <returns>The method's own result, or the target ids and, when asked, the rows read back.</returns>
        private async Task<object?> RunGeneralAsync()
        {
            using ConfirmationFrame frame = Services.Confirmations.Frame(Resource, OperationName);
            ValidationScheduler scheduler = new(_stack.Limits.MaxValidationRounds, OperationName, DispatchAsync, Services.Metrics, _cancellationToken);
            _scheduler = scheduler;
            _loader = new ContextLoader(Services.Metadata, _map, scheduler, ReadDecision);
            _recording = RecordingBatch.Deferred(CallbackFailed, PersistGate.EnsureComposing);

            // The ids are known, so the frame composes from the start; a flow whose task read
            // Persisted composes nothing more, an enlistment included.
            _frame = new PipelineFrame(Services, _stack, OperationName, frame, OpenWriteFrame.SourceOf(Services.Accessor.Current), PersistGate.EnsureComposing, _descriptor.Name);
            using IDisposable flow = FrameFlow.Enter(_frame);
            _frame.BeginComposing(scheduler, _loader, _map, _errors, _cancellationToken);
            _context = NewContext(_loader, _errors, _frame, _recording, scheduler, tracked: true);
            try
            {
                // --- Round trip 1 carries the targets; the tasks join it with their first loads ---
                IReadOnlyList<string> children = _descriptor.LoadChildren ? Collections(_root) : [];
                _loader.Enqueue(this, new PipelineLoad(batch => ComposeTargets<TEntity, TKey>(
                    batch, Composer, _map, _root, _ids, _descriptor.Action, children, (rows, granted) => _loaded = (rows, granted))));
                scheduler.RequestRound();
                Start(scheduler, _context, withMethod: true);
                _ = await scheduler.RunAsync().ConfigureAwait(false);

                // --- Composing closes, the enlisted groups' with it, then the outcome in the order a
                // caller can act on ---
                _context.Close();
                ThrowIfReleasedFlowFailed();
                _recording.Seal();
                _frame.Close(complete: true);
                ValidationOutcome.Raise(staleStamp: null, _errors, frame);
                Compose(_context);
                object? result = await PersistAsync().ConfigureAwait(false);
                Enlistments?.Count(failure: null);
                return result;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // What ended the action ends a method waiting for its persist; a committed action's
                // Persisted already completed, so this changes nothing then.
                _ = _context.FaultPersisted(failure);
                Observe(_methodTask);
                Enlistments?.Count(failure);
                throw;
            }
            finally
            {
                // The groups enlisted with the action return the ids they reserved and never sent.
                _frame.Close();
                Enlistments?.Release();
            }
        }

        /// <summary>The context the hook, the components and the method see.</summary>
        /// <param name="loader">The loader.</param>
        /// <param name="errors">The errors.</param>
        /// <param name="host">The frame the context's tasks see as their host.</param>
        /// <param name="batch">The batch the context's appends are recorded on.</param>
        /// <param name="scheduler">The rounds the context's tasks run in.</param>
        /// <param name="tracked">Whether the context's refusals are recorded on the run: the first run's, never a re-run's.</param>
        /// <returns>The context.</returns>
        private ActionContext<TEntity, TKey> NewContext(ContextLoader loader, ValidationErrors<TEntity> errors, IWriteHost host, RecordingBatch batch, ValidationScheduler scheduler, bool tracked)
        {
            Action<Exception>? late = tracked ? RecordLate : null;
            return new ActionContext<TEntity, TKey>
            {
                Ids = _ids,
                Arguments = _arguments,
                Context = Services.Accessor.Current,
                Loader = loader,
                Errors = errors,
                Batch = batch,
                Host = host,
                CancellationToken = _cancellationToken,
                Resource = Resource,
                Notifier = Services.Services.GetService<INotifier>(),
                AssignmentsService = Services.Services.GetService<IAssignments>(),
                PersistedRead = () => ReadPersisted(scheduler, late),
                ComposeGuard = PersistGate.EnsureComposing,
                Settle = scheduler.SettleAsync,
            };
        }

        /// <summary>Starts the service's action hook, every component's, and, when asked, the method, as tasks of the rounds.</summary>
        /// <param name="scheduler">The rounds' scheduler.</param>
        /// <param name="context">The context the tasks see.</param>
        /// <param name="withMethod">Whether the method runs: on the first run, never on a re-run.</param>
        private void Start(ValidationScheduler scheduler, ActionContext<TEntity, TKey> context, bool withMethod)
        {
            // --- A re-run's flows wait on a context that never persists, so only the first run's count;
            // the first run's tasks run inside the action's frame. The hook and the components run at
            // the same slots on both, so a re-run's enlistments are matched to the first run's by the
            // task that made them; the method, which a re-run never invokes, runs at none ---
            string name = _descriptor.Name;
            EnlistmentFrame? frame = withMethod ? _frame : null;
            Gated(scheduler, _behavior.GetType().Name, InFrame(frame, () => _behavior.ValidateActionAsync(name, context)), tracked: withMethod, slot: 0);
            int slot = 1;
            foreach (IEntityValidator<TEntity> validator in Services.Services.GetServices<IEntityValidator<TEntity>>())
            {
                Gated(scheduler, validator.GetType().Name, InFrame(frame, () => validator.ValidateActionAsync(name, context)), tracked: withMethod, slot++);
            }

            if (withMethod && _method is MethodInfo method)
            {
                Gated(scheduler, method.Name, InFrame(frame, () => _methodTask = InvokeAsync(method, context)), tracked: true, slot: null);
            }
        }

        /// <summary>A task's body, run inside a frame when one is given.</summary>
        /// <param name="frame">The frame; null to run the body as it is.</param>
        /// <param name="body">The body.</param>
        /// <returns>The body to run.</returns>
        private static Func<Task> InFrame(EnlistmentFrame? frame, Func<Task> body)
        {
            return frame is null ? body : () => FrameFlow.RunInAsync(frame, body);
        }

        /// <summary>
        ///     Runs a task of the rounds that leaves them when it reads <see cref="ActionContext{TEntity}.Persisted" />:
        ///     the rounds then end without waiting for it, so the persist it waits for can run.
        /// </summary>
        /// <param name="scheduler">The rounds' scheduler.</param>
        /// <param name="name">The task's name.</param>
        /// <param name="body">The task.</param>
        /// <param name="tracked">
        ///     Whether the flow, once it left the rounds, abandons the action by failing before the persist
        ///     is sent, is awaited once it committed, and has its refusals recorded on the run.
        /// </param>
        /// <param name="slot">The task's slot among the action's own tasks; null for the action's method, which the commit awaits on its own.</param>
        private void Gated(ValidationScheduler scheduler, string name, Func<Task> body, bool tracked, int? slot)
        {
            bool isMethod = slot is null;
            scheduler.Run(name, async () =>
            {
                // The gate flows into the task's body, and no further than it.
                PersistGate gate = new(scheduler, tracked ? RecordLate : null);
                Waiting.Value = gate;
                Task flow = body();
                if (await Task.WhenAny(flow, gate.Released).ConfigureAwait(false) == flow)
                {
                    await flow.ConfigureAwait(false);
                    return;
                }

                // Left the rounds for the persist: the flow resumes once the action committed or ended.
                if (tracked)
                {
                    _released.Enqueue(new ReleasedFlow(name, flow, isMethod));
                }

                Observe(flow);
            }, slot);
        }

        /// <summary>
        ///     Abandons the action unsent when a task composed after reading Persisted, whichever flow
        ///     of it did, or a flow that left the rounds to wait for the persist has failed already.
        /// </summary>
        private void ThrowIfReleasedFlowFailed()
        {
            // --- A refusal recorded from any flow, the ones nobody awaits included ---
            if (LateRefusal() is Exception late)
            {
                ExceptionDispatchInfo.Throw(late);
            }

            // --- A released flow's own failure: the first of its exceptions, however many it holds ---
            foreach (ReleasedFlow released in _released)
            {
                if (released.Flow is { IsFaulted: true, Exception: AggregateException { InnerExceptions: [Exception failure, ..] } })
                {
                    ExceptionDispatchInfo.Throw(failure);
                }

                if (released.Flow.IsCanceled)
                {
                    throw new OperationCanceledException("A task of the action that left its validation rounds was cancelled before the action persisted.");
                }
            }
        }

        /// <summary>
        ///     The first write the run's tasks composed after reading Persisted: a refused save, update,
        ///     append or load, recorded whichever flow raised it, or an error reported once the rounds
        ///     ended; null when none was.
        /// </summary>
        /// <returns>The refusal; null when none was.</returns>
        private Exception? ComposedLate()
        {
            return LateRefusal()
                ?? (_context is not null && _errors.HasErrors
                    ? new InvalidOperationException("A task of the action reported a validation error after the validation rounds ended, after reading Persisted. Report every error before reading Persisted.")
                    : null);
        }

        /// <summary>The first refusal of a save, an update, an append or a load a task composed after reading Persisted; null when none was.</summary>
        /// <returns>The refusal; null when none was.</returns>
        private Exception? LateRefusal()
        {
            return Volatile.Read(ref _composedLate) ?? _scheduler?.LateFailure;
        }

        /// <summary>Records the first refusal of a write a task composed after reading Persisted.</summary>
        /// <param name="refusal">The refusal.</param>
        private void RecordLate(Exception refusal)
        {
            _ = Interlocked.CompareExchange(ref _composedLate, refusal, null);
        }

        // Spec 0015 §11.2: the hook's and the components' flows that waited for the persist resume
        // once it committed, as the method's does; a failure of theirs then is the committed action's
        // to log, and a refused late write is raised with the answer.
        /// <summary>Waits for every released flow of the hook and the components, logging a failure after the commit.</summary>
        /// <returns>A task that completes when every released flow returned.</returns>
        private async Task ResumeReleasedAsync()
        {
            foreach (ReleasedFlow released in _released)
            {
                if (released.IsMethod)
                {
                    continue;
                }

                try
                {
                    await released.Flow.ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    // A refused late write is raised with the answer, and logged there.
                    if (!ReferenceEquals(failure, LateRefusal()))
                    {
                        CrudLog.ActionTaskFailedAfterCommit(Services.Logger, released.Name, Resource, OperationName, Activity.Current?.TraceId.ToString(), failure);
                    }
                }
            }
        }

        /// <summary>Tells the gate of the reading flow's task that it reads Persisted.</summary>
        /// <param name="scheduler">The rounds the context's tasks run in.</param>
        /// <param name="late">Records the refusal on the run; null for a re-run.</param>
        /// <exception cref="InvalidOperationException">
        ///     The flow carries no task's context while the rounds run, so its task could never leave
        ///     them; or another flow of its task is parked at a load.
        /// </exception>
        private static void ReadPersisted(ValidationScheduler scheduler, Action<Exception>? late)
        {
            if (Waiting.Value is PersistGate gate)
            {
                gate.Read();
                return;
            }

            // A flow started with the execution context suppressed carries no gate: its task would
            // never leave the rounds, and the persist it waits for would never run.
            if (scheduler.IsRunning)
            {
                InvalidOperationException refusal = new(
                    "Persisted was read during the action's validation rounds from a flow that carries no task's context, such as one started with the execution context's flow suppressed. Read Persisted from the task's own flows.");
                late?.Invoke(refusal);
                throw refusal;
            }
        }

        /// <summary>Invokes the action's method, keeping its result, or the partial failure it threw after composing.</summary>
        /// <param name="method">The method.</param>
        /// <param name="context">The context it takes.</param>
        /// <returns>A task that completes when the method returned.</returns>
        private async Task InvokeAsync(MethodInfo method, ActionContext<TEntity, TKey> context)
        {
            object?[] parameters = method.GetParameters().Length == 2 ? [context, _arguments] : [context];
            try
            {
                _result = await ActionInvoker.InvokeAsync(method, _behavior, parameters).ConfigureAwait(false);
            }
            catch (PartialFailureException partial)
            {
                // The one exception that does not abort the action: what the method composed persists.
                _partial = ExceptionDispatchInfo.Capture(partial);
            }
        }

        /// <summary>
        ///     Dispatches one round; after the first, settles the targets before any task resumes; then
        ///     the checks of every group enlisted with the action that the round served.
        /// </summary>
        /// <param name="round">The round's 1-based number.</param>
        /// <param name="cancellationToken">Cancels the round.</param>
        /// <returns>A task that completes when the round's refs are filled.</returns>
        private async Task DispatchAsync(int round, CancellationToken cancellationToken)
        {
            await DispatchRoundAsync(_loader!, cancellationToken).ConfigureAwait(false);
            if (round == 1)
            {
                // An absent or hidden id is not found, before a row outside the grant is forbidden.
                _targets = SettleTargets(_ids, _loaded.Rows, _loaded.Granted, _descriptor.Action);
                _context!.Entities = _targets;
            }

            if (Enlistments is EnlistmentRounds enlistments)
            {
                await enlistments.AfterRoundAsync(round).ConfigureAwait(false);
            }
        }

        // Spec 0015 §5.1 step 1 of a re-entry: the hook and the components run again over the loaded
        // targets and the fresh user, in memory, on a context whose composition is discarded; the
        // method is never invoked again, and what it composed is replayed. A new error or a new
        // unconfirmed warning refuses the persist; round trip 1's verdicts stand, and the update and
        // the emitter check the targets again under the fresh grants inside the transaction.
        /// <summary>Re-runs the action's validators in memory for the fresh connected user.</summary>
        /// <param name="user">The fresh connected user.</param>
        /// <returns>A task that completes when every validator returned.</returns>
        /// <exception cref="StaleContextException">A tag the rounds were served under moved, or a validator asked for a load the rounds did not serve.</exception>
        /// <exception cref="ValidationException">A validator reported an error.</exception>
        /// <exception cref="ConfirmationRequiredException">A validator raised an unconfirmed warning.</exception>
        private async Task RevalidateAsync(ConnectedUser user)
        {
            EnsureServedTagsHold(user);
            Composer = user;
            _loader!.AnswerOnly();
            using ConfirmationFrame frame = Services.Confirmations.Frame(Resource, OperationName, audited: false);
            ValidationErrors<TEntity> errors = new();
            ValidationScheduler scheduler = new(
                _stack.Limits.MaxValidationRounds,
                OperationName,
                static (_, _) => throw new InvalidOperationException("A re-run of the validators answers from the rounds already run and dispatches none."),
                metrics: null,
                _cancellationToken);
            RerunHost host = new(_frame!, scheduler, frame, Services.Metadata);
            ActionContext<TEntity, TKey> context = NewContext(_loader, errors, host, RecordingBatch.Deferred(), scheduler, tracked: false);
            context.Entities = _targets;

            // --- The validators, with every round trip refused as stale ---
            try
            {
                using (ComposeFence.Close(static () => new StaleContextException([])))
                {
                    Start(scheduler, context, withMethod: false);
                    _ = await scheduler.RunAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                // The re-run's composition is discarded, and its persist never runs.
                context.Close();
                _ = context.FaultPersisted(new OperationCanceledException("A re-run of an action's validators persists nothing of its own."));
            }

            // --- What they report, then every first-run enlistment of theirs matched by its task ---
            ValidationOutcome.Raise(staleStamp: null, errors, frame);
            host.EnsureMatched();
        }

        /// <summary>
        ///     The gate of one task of the rounds. When a flow of the task reads
        ///     <see cref="ActionContext{TEntity}.Persisted" />, the task is leaving: it composes nothing
        ///     more, and it leaves the rounds once that flow stopped running on its thread, so a refusal
        ///     raised before then fails the rounds rather than race the persist.
        /// </summary>
        /// <param name="scheduler">The rounds the task runs in.</param>
        /// <param name="late">Records a refusal on the run; null for a re-run's task.</param>
        private sealed class PersistGate(ValidationScheduler scheduler, Action<Exception>? late)
        {
            /// <summary>Guards the leave, which flows of the task on several threads may race.</summary>
            private readonly Lock _lock = new();

            /// <summary>Completes when the task left the rounds.</summary>
            private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Takes the task out of the rounds; null until a flow read Persisted, and once it ran.</summary>
            private Action? _leave;

            /// <summary>Whether a flow of the task read Persisted.</summary>
            private volatile bool _leaving;

            /// <summary>Completes when the task left the rounds.</summary>
            public Task Released => _released.Task;

            /// <summary>Whether a flow of the task read Persisted: the task composes nothing more.</summary>
            public bool IsLeaving => _leaving;

            /// <summary>Records a refusal on the run; null for a re-run's task.</summary>
            private Action<Exception>? Late { get; } = late;

            /// <summary>Refuses a save, an update or an append from a flow whose task read Persisted, recording the refusal on the run.</summary>
            /// <exception cref="InvalidOperationException">The calling flow's task read Persisted.</exception>
            public static void EnsureComposing()
            {
                if (Waiting.Value is { IsLeaving: true } gate)
                {
                    InvalidOperationException refusal = new(
                        "The action's task read Persisted and then composed more: a save, an update or an append to the action's batch after reading Persisted is refused. Read Persisted where it is awaited, after every load and every write is composed.");
                    gate.Late?.Invoke(refusal);
                    throw refusal;
                }
            }

            /// <summary>Marks the task as leaving the rounds, when a flow of it reads Persisted.</summary>
            /// <exception cref="InvalidOperationException">Another flow of the task is parked at a load.</exception>
            public void Read()
            {
                lock (_lock)
                {
                    if (_leaving)
                    {
                        return;
                    }

                    _leave = scheduler.BeginLeave();
                    _leaving = true;
                }
            }

            /// <summary>Takes the leaving task out of the rounds, then releases the gate; runs as the thread switches context, so it never throws.</summary>
            public void Left()
            {
                Action? leave;
                lock (_lock)
                {
                    leave = _leave;
                    _leave = null;
                }

                if (leave is not null)
                {
                    // Under the scheduler's own lock; it raises nothing.
                    leave();
                    _ = _released.TrySetResult();
                }
            }
        }

        /// <summary>A flow that left the rounds to wait for the persist.</summary>
        /// <param name="Name">The task's name.</param>
        /// <param name="Flow">The flow.</param>
        /// <param name="IsMethod">Whether the task is the action's method.</param>
        private readonly record struct ReleasedFlow(string Name, Task Flow, bool IsMethod);

        /// <summary>Observes a task's fault, so a flow nobody awaits leaves no unobserved exception.</summary>
        /// <param name="task">The task; null for none.</param>
        private static void Observe(Task? task)
        {
            _ = task?.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
