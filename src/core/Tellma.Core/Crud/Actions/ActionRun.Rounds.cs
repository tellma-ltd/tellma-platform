// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Crud.Actions
{
    // Spec 0015 §11.2 steps 2 and 3: round trip 1 carries the targets under the caller's Read filter,
    // with every child collection when the action loads children, and their count under the action's
    // grant, beside every load the hook, the components and the method request before their first
    // await. The three run as tasks of the validation rounds over one ActionContext. The method
    // composes before the persist batch exists: its Batch is a deferred recording replayed onto the
    // persist, and a task that reads Persisted leaves the rounds, so the persist it waits for runs
    // once every other task completed or parked (§11.2, reading its own writes). A method that throws
    // PartialFailureException leaves what it composed to be persisted, and the exception to be
    // rethrown once the action committed.
    /// <summary>One action of one stack: the general path's context, its tasks and their rounds.</summary>
    internal sealed partial class ActionRun<TEntity, TKey>
    {
        /// <summary>The gate of the task the current flow runs in: completed when the task reads <see cref="ActionContext{TEntity}.Persisted" />.</summary>
        private static readonly AsyncLocal<TaskCompletionSource?> Waiting = new();

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

        /// <summary>Runs the general path: round trip 1, the validators and the method with their rounds, then the persist.</summary>
        /// <returns>The method's own result, or the target ids and, when asked, the rows read back.</returns>
        private async Task<object?> RunGeneralAsync()
        {
            using ConfirmationFrame frame = Services.Confirmations.Frame(Resource, OperationName);
            ValidationScheduler scheduler = new(_stack.Limits.MaxValidationRounds, OperationName, DispatchAsync, Services.Metrics, _cancellationToken);
            _loader = new ContextLoader(Services.Metadata, _map, scheduler, ReadDecision);
            _recording = RecordingBatch.Deferred(CallbackFailed);
            _context = NewContext(_loader, _errors, frame, _recording);
            try
            {
                // --- Round trip 1 carries the targets; the tasks join it with their first loads ---
                IReadOnlyList<string> children = _descriptor.LoadChildren ? Collections(_root) : [];
                _loader.Enqueue(this, new PipelineLoad(batch => ComposeTargets<TEntity, TKey>(
                    batch, Composer, _map, _root, _ids, _descriptor.Action, children, (rows, granted) => _loaded = (rows, granted))));
                scheduler.RequestRound();
                Start(scheduler, _context, withMethod: true);
                _ = await scheduler.RunAsync().ConfigureAwait(false);

                // --- Composing closes, then the outcome in the order a caller can act on ---
                _context.Close();
                _recording.Seal();
                ValidationOutcome.Raise(staleStamp: null, _errors, frame);
                Compose(_context);
                return await PersistAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // What ended the action ends a method waiting for its persist; a committed action's
                // Persisted already completed, so this changes nothing then.
                _ = _context.FaultPersisted(failure);
                Observe(_methodTask);
                throw;
            }
        }

        /// <summary>The context the hook, the components and the method see.</summary>
        /// <param name="loader">The loader.</param>
        /// <param name="errors">The errors.</param>
        /// <param name="frame">The confirmation frame.</param>
        /// <param name="batch">The batch the context's appends are recorded on.</param>
        /// <returns>The context.</returns>
        private ActionContext<TEntity, TKey> NewContext(ContextLoader loader, ValidationErrors<TEntity> errors, ConfirmationFrame frame, RecordingBatch batch)
        {
            return new ActionContext<TEntity, TKey>
            {
                Ids = _ids,
                Arguments = _arguments,
                Context = Services.Accessor.Current,
                Loader = loader,
                Errors = errors,
                Batch = batch,
                Host = new PipelineWriteHost(Resource, OperationName, frame),
                CancellationToken = _cancellationToken,
                Resource = Resource,
                Notifier = Services.Services.GetService<INotifier>(),
                AssignmentsService = Services.Services.GetService<IAssignments>(),
                PersistedRead = static () => Waiting.Value?.TrySetResult(),
            };
        }

        /// <summary>Starts the service's action hook, every component's, and, when asked, the method, as tasks of the rounds.</summary>
        /// <param name="scheduler">The rounds' scheduler.</param>
        /// <param name="context">The context the tasks see.</param>
        /// <param name="withMethod">Whether the method runs: on the first run, never on a re-run.</param>
        private void Start(ValidationScheduler scheduler, ActionContext<TEntity, TKey> context, bool withMethod)
        {
            string name = _descriptor.Name;
            Gated(scheduler, _behavior.GetType().Name, () => _behavior.ValidateActionAsync(name, context));
            foreach (IEntityValidator<TEntity> validator in Services.Services.GetServices<IEntityValidator<TEntity>>())
            {
                Gated(scheduler, validator.GetType().Name, () => validator.ValidateActionAsync(name, context));
            }

            if (withMethod && _method is MethodInfo method)
            {
                Gated(scheduler, method.Name, () => _methodTask = InvokeAsync(method, context));
            }
        }

        /// <summary>
        ///     Runs a task of the rounds that leaves them when it reads <see cref="ActionContext{TEntity}.Persisted" />:
        ///     the rounds then end without waiting for it, so the persist it waits for can run.
        /// </summary>
        /// <param name="scheduler">The rounds' scheduler.</param>
        /// <param name="name">The task's name.</param>
        /// <param name="body">The task.</param>
        private static void Gated(ValidationScheduler scheduler, string name, Func<Task> body)
        {
            scheduler.Run(name, async () =>
            {
                // The gate flows into the task's body, and no further than it.
                TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Waiting.Value = gate;
                Task flow = body();
                if (await Task.WhenAny(flow, gate.Task).ConfigureAwait(false) == flow)
                {
                    await flow.ConfigureAwait(false);
                    return;
                }

                // Parked on the persist: the flow resumes once the action committed or ended.
                Observe(flow);
            });
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

        /// <summary>Dispatches one round; after the first, settles the targets before any task resumes.</summary>
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
            ActionContext<TEntity, TKey> context = NewContext(_loader, errors, frame, RecordingBatch.Deferred());
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

            ValidationOutcome.Raise(staleStamp: null, errors, frame);
        }

        /// <summary>Observes a task's fault, so a flow nobody awaits leaves no unobserved exception.</summary>
        /// <param name="task">The task; null for none.</param>
        private static void Observe(Task? task)
        {
            _ = task?.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
