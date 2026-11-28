// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.ExceptionServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §13.3: an open frame is the frame of an API action, a job's handler or a provisioning
    // step, which the work inside it persists itself. It composes nothing of its own: the writes
    // enlisted with it are its groups, their stages tasks of its own rounds, and its PersistAsync runs
    // what rounds are pending, then sends every group in one persist batch. The work runs as one task
    // of the rounds, so a load it waits for (an enlisted save's ids) rides the next round; it leaves
    // the rounds when it calls PersistAsync, and the rounds then end once every group's tasks did. An
    // operation the work calls through the front door runs its own pipeline frame inside this one,
    // whose persist this frame's waits for. The frame is registered for the scope's persists, so a
    // tenant-state verdict judges a nested persist by the frame (§5.1).
    /// <summary>The frame of an API action, a job's handler or a provisioning step.</summary>
    internal sealed partial class OpenWriteFrame : EnlistmentFrame, IOpenWriteHost
    {
        /// <summary>The scope's write services.</summary>
        private readonly SaveOperation _services;

        /// <summary>The frame as the scope's persists see it.</summary>
        private readonly WriteFrame _descriptor;

        /// <summary>The resource the frame's confirmations and logs name.</summary>
        private readonly string _resource;

        /// <summary>The <c>crud.operation</c> the frame's rounds and enlistments are counted under.</summary>
        private readonly string _metricsOperation;

        /// <summary>The class the frame was opened for: the method's declaring class, the handler or the step.</summary>
        private readonly Type _openedFor;

        /// <summary>The stack the class is attached to, whose own rows it writes through a self-pair; null when it is attached to none.</summary>
        private readonly StackDescriptor? _ownStack;

        /// <summary>Whether a self-pair is admitted: only on the frame of an API action.</summary>
        private readonly bool _admitsSelfPair;

        /// <summary>The job's completion batch the groups are appended to; null for a frame that persists itself.</summary>
        private readonly IDataBatch? _completion;

        /// <summary>The API action's securable, which every persist composition decides again; null for a member-only action, a job or a step.</summary>
        private readonly SecurableRef? _securable;

        /// <summary>Cancels the frame's rounds and persist: the frame's own token, and the token its persist is called with.</summary>
        private readonly CancellationTokenSource _cancellation;

        /// <summary>The write steps the frame shares with the pipeline: its rounds' dispatch and its persist's decisions.</summary>
        private readonly FrameRun _run;

        /// <summary>The frame's errors, which every group re-homes its own into.</summary>
        private readonly ValidationErrors _errors = new();

        /// <summary>Completes when the work left the rounds to persist.</summary>
        private readonly TaskCompletionSource _left = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     Completes when the frame's rounds ended, with their failure or null. The work may persist
        ///     before the rounds' own task is in hand, since its first steps run as the rounds start.
        /// </summary>
        private readonly TaskCompletionSource<ExceptionDispatchInfo?> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The frame's confirmations, once it runs.</summary>
        private ConfirmationFrame? _confirmations;

        /// <summary>The frame's rounds, once it runs.</summary>
        private EnlistmentRounds? _rounds;

        /// <summary>The task of the rounds the work runs as, which alone may persist the frame.</summary>
        private object? _work;

        /// <summary>The persists asked for: a frame persists once.</summary>
        private int _persists;

        /// <summary>Whether the work asked to persist, so it enlists nothing more.</summary>
        private volatile bool _persisting;

        /// <summary>The pipeline operations nested in the frame that are composing.</summary>
        private int _nested;

        /// <summary>Opens a frame.</summary>
        /// <param name="services">The scope's write services.</param>
        /// <param name="descriptor">The frame as the scope's persists see it.</param>
        /// <param name="resource">The resource the frame's confirmations and logs name.</param>
        /// <param name="metricsOperation">The <c>crud.operation</c> the frame's rounds and enlistments are counted under.</param>
        /// <param name="openedFor">The class the frame was opened for.</param>
        /// <param name="admitsSelfPair">Whether a self-pair is admitted: on the frame of an API action alone.</param>
        /// <param name="completion">The job's completion batch the groups are appended to; null for a frame that persists itself.</param>
        /// <param name="securable">The API action's securable; null for a member-only action, a job or a step.</param>
        /// <param name="cancellationToken">The frame's own token, such as the request's or a job's lease.</param>
        private OpenWriteFrame(
            SaveOperation services,
            WriteFrame descriptor,
            string resource,
            string metricsOperation,
            Type openedFor,
            bool admitsSelfPair,
            IDataBatch? completion,
            SecurableRef? securable,
            CancellationToken cancellationToken)
        {
            _securable = securable;
            _services = services;
            _descriptor = descriptor;
            _resource = resource;
            _metricsOperation = metricsOperation;
            _openedFor = openedFor;
            _ownStack = services.Registry.StackOfWriter(openedFor);
            _admitsSelfPair = admitsSelfPair;
            _completion = completion;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _run = new FrameRun(services, resource, metricsOperation);
        }

        /// <inheritdoc />
        public override WriteHostKind Kind => _descriptor.Kind;

        /// <inheritdoc />
        public override string Operation => _descriptor.Operation;

        /// <inheritdoc />
        public override IConfirmations Confirmations => _confirmations ?? throw NotRunning();

        /// <inheritdoc />
        public override EnlistmentRounds Rounds => _rounds ?? throw NotRunning();

        /// <inheritdoc />
        public override ValidationErrors Errors => _errors;

        /// <inheritdoc />
        public override SaveSource Source => _descriptor.Kind == WriteHostKind.ApiAction ? SourceOf(_services.Accessor.Current) : SaveSource.System;

        /// <inheritdoc />
        public override bool IsComposing => false;

        /// <summary>Opens the frame of an API action, which persists itself.</summary>
        /// <param name="services">The scope's write services.</param>
        /// <param name="action">The action.</param>
        /// <param name="method">The action's method and the resource it is declared under.</param>
        /// <param name="cancellationToken">The invocation's token.</param>
        /// <returns>The frame, not yet running.</returns>
        public static OpenWriteFrame ForApiAction(SaveOperation services, ApiActionDescriptor action, ActionMethod method, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(method);
            WriteFrame descriptor = new(WriteHostKind.ApiAction, $"{method.Owner}:{action.Name}", AdmitsReadOnly: !action.Mutation || action.AllowWhenReadOnly);
            return new OpenWriteFrame(services, descriptor, method.Owner, CrudOperationNames.ApiAction, method.Handler, admitsSelfPair: true, completion: null, action.Securable, cancellationToken);
        }

        /// <summary>Opens the frame of a provisioning step, which persists itself under the system scope.</summary>
        /// <param name="services">The scope's write services.</param>
        /// <param name="step">The step's name.</param>
        /// <param name="stepType">The step's class.</param>
        /// <param name="cancellationToken">The step's token.</param>
        /// <returns>The frame, not yet running.</returns>
        public static OpenWriteFrame ForProvisioning(SaveOperation services, string step, Type stepType, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(step);
            ArgumentNullException.ThrowIfNull(stepType);
            WriteFrame descriptor = new(WriteHostKind.Provisioning, $"provisioning:{step}", AdmitsReadOnly: true);
            return new OpenWriteFrame(services, descriptor, ProvisioningResource, CrudOperationNames.Provisioning, stepType, admitsSelfPair: false, completion: null, securable: null, cancellationToken);
        }

        // Spec 0015 §13.3 and spec 0020 §3.1: a job's frame appends its groups to the job's completion
        // batch, after the statement that records the outcomes, so they commit with the outcome and
        // roll back with a lost lease. The worker composes and sends that batch; the frame runs its
        // groups' post-commit phase once the worker reports the commit.
        /// <summary>Opens the frame of a job's handler, whose groups the job's completion batch carries.</summary>
        /// <param name="services">The scope's write services.</param>
        /// <param name="key">The job's key.</param>
        /// <param name="handler">The handler's class.</param>
        /// <param name="runsWhenReadOnly">Whether the handler runs on a read-only tenant.</param>
        /// <param name="completion">The job's completion batch.</param>
        /// <param name="cancellationToken">The job's lease.</param>
        /// <returns>The frame, not yet running.</returns>
        public static OpenWriteFrame ForJob(SaveOperation services, string key, Type handler, bool runsWhenReadOnly, IDataBatch completion, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(completion);
            WriteFrame descriptor = new(WriteHostKind.Job, $"job:{key}", runsWhenReadOnly);
            return new OpenWriteFrame(services, descriptor, JobResource, CrudOperationNames.Job, handler, admitsSelfPair: false, completion, securable: null, cancellationToken);
        }

        /// <summary>
        ///     Runs the frame's work: the method, the handler or the step, as a task of the frame's
        ///     rounds, inside the frame. The frame is the scope's open frame and the flow's innermost
        ///     until the work completed.
        /// </summary>
        /// <typeparam name="TResult">What the work returns.</typeparam>
        /// <param name="work">The work.</param>
        /// <returns>What the work returned.</returns>
        /// <exception cref="InvalidOperationException">The frame ran already, or the work completed with enlisted writes it never persisted.</exception>
        public async Task<TResult> RunAsync<TResult>(Func<Task<TResult>> work)
        {
            ArgumentNullException.ThrowIfNull(work);
            if (_rounds is not null)
            {
                throw new InvalidOperationException($"The frame of {Operation} runs once.");
            }

            // --- The frame: the scope's open frame for its persists, the flow's for its writes ---
            using IDisposable entered = FrameFlow.Enter(this);
            using IDisposable registered = _services.Frames.Enter(_descriptor);
            using ConfirmationFrame confirmations = _services.Confirmations.Frame(_resource, _descriptor.Operation);
            _confirmations = confirmations;
            try
            {
                Open();

                // --- The work, one task of the rounds, which leaves them to persist ---
                TaskCompletionSource<Task<TResult>> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Rounds.Scheduler.Run(_openedFor.Name, () => WorkAsync(work, started));

                // --- The rounds end once the work completed or left them, and every group's task completed.
                // A persist waiting on them learns how they ended ---
                ExceptionDispatchInfo? failed = null;
                try
                {
                    _ = await Rounds.Scheduler.RunAsync().ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    failed = ExceptionDispatchInfo.Capture(failure);
                }

                _ = _ended.TrySetResult(failed);

                // --- The work then completes: inside the rounds already, or once its persist answered.
                // A failure of the rounds is the frame's, whatever the work made of it ---
                Task<TResult> flow = await started.Task.ConfigureAwait(false);
                if (failed is not null)
                {
                    await Observed(flow).ConfigureAwait(false);
                    failed.Throw();
                }

                TResult result = await flow.ConfigureAwait(false);
                if (!AppendedToCompletion)
                {
                    Rounds.Count(failure: null);
                }

                return result;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                if (!AppendedToCompletion)
                {
                    _rounds?.Count(failure);
                }

                throw;
            }
            finally
            {
                // A write never sent returns its ids; nothing enlists once the frame left. Groups on a
                // job's completion batch keep their ids until the worker sent or dropped the batch,
                // which it reports through EndCompletion.
                _rounds?.Close(complete: false);
                if (!AppendedToCompletion)
                {
                    _rounds?.Release();
                }

                _cancellation.Dispose();
            }
        }

        /// <summary>
        ///     Tells the frame an operation nested in it started or stopped composing: the frame's
        ///     persist is refused while one composes.
        /// </summary>
        /// <param name="entering">True when the operation started composing; false when it stopped.</param>
        public void NestedComposing(bool entering)
        {
            _ = entering ? Interlocked.Increment(ref _nested) : Interlocked.Decrement(ref _nested);
        }

        /// <inheritdoc />
        protected override void EnsureAdmits(Type writer)
        {
            if (_rounds is null || _persisting || Rounds.IsClosed)
            {
                throw new InvalidOperationException(
                    $"The class {writer.Name} enlisted a write with {Operation} after it persisted. An open frame admits enlistment until its PersistAsync.");
            }
        }

        /// <inheritdoc />
        protected override bool AdmitsSelfPair(StackDescriptor target)
        {
            return _admitsSelfPair && _ownStack is StackDescriptor own && string.Equals(own.Resource, target.Resource, StringComparison.Ordinal);
        }

        /// <summary>Opens the frame's rounds: its scheduler, its loader over the tenant's schema, and the participants it was opened for.</summary>
        /// <exception cref="InvalidOperationException">The tenant's settings are not loaded in the scope.</exception>
        private void Open()
        {
            MultilingualShape shape = _services.Accessor.Current.TenantSettings?.Shape
                ?? throw new InvalidOperationException($"The tenant's settings are not loaded in this scope, so the frame of {Operation} has no schema to validate under.");
            QueryexSchemaMap map = _services.Schemas.Map(shape);
            int ceiling = _ownStack?.Limits.MaxValidationRounds ?? StackLimits.Default.MaxValidationRounds;
            ValidationScheduler scheduler = new(ceiling, _metricsOperation, DispatchAsync, _services.Metrics, _cancellation.Token);
            ContextLoader loader = new(_services.Metadata, map, scheduler, _run.ReadDecisionOf);
            HashSet<Type> participants = [_openedFor];
            _rounds = new EnlistmentRounds(_services, scheduler, loader, map, _metricsOperation, participants, _cancellation.Token);
        }

        /// <summary>Runs the work as a task of the rounds, until it completes or leaves them to persist.</summary>
        /// <typeparam name="TResult">What the work returns.</typeparam>
        /// <param name="work">The work.</param>
        /// <param name="started">Receives the work's task.</param>
        /// <returns>A task that completes when the work completed or left the rounds.</returns>
        /// <exception cref="InvalidOperationException">The work completed with enlisted writes it never persisted.</exception>
        private async Task WorkAsync<TResult>(Func<Task<TResult>> work, TaskCompletionSource<Task<TResult>> started)
        {
            _work = Rounds.Scheduler.CurrentTask;
            Task<TResult> flow;
            try
            {
                flow = work();
            }
            catch (Exception failure)
            {
                flow = Task.FromException<TResult>(failure);
            }

            _ = started.TrySetResult(flow);
            if (await Task.WhenAny(flow, _left.Task).ConfigureAwait(false) != flow)
            {
                // Left the rounds to persist: the work resumes once its persist ran.
                return;
            }

            // --- Completed inside the rounds: its failure is the rounds', and an unpersisted write an authoring bug ---
            _ = await flow.ConfigureAwait(false);
            if (Groups.Count > 0 && !_persisting)
            {
                throw new InvalidOperationException(
                    $"The work of {Operation} returned with writes enlisted with its frame that it never persisted. Await PersistAsync on the injected IOpenWriteHost before returning.");
            }
        }

        /// <summary>Dispatches one round of the frame: the loads on one guarded batch, then what each group does after it.</summary>
        /// <param name="round">The round's 1-based number.</param>
        /// <param name="cancellationToken">Cancels the round.</param>
        /// <returns>A task that completes when the round's refs are filled and the groups' checks ran.</returns>
        private async Task DispatchAsync(int round, CancellationToken cancellationToken)
        {
            await _run.DispatchAsync(Rounds.Loader, cancellationToken).ConfigureAwait(false);
            await Rounds.AfterRoundAsync(round).ConfigureAwait(false);
        }

        /// <summary>Who a caller's writes are recorded as coming from: the platform, a service account, an agent or the web.</summary>
        /// <param name="context">The caller's request context.</param>
        /// <returns>The source.</returns>
        public static SaveSource SourceOf(RequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.IsSystem ? SaveSource.System
                : context.Kind == PrincipalKind.ServiceAccount ? SaveSource.PublicApi
                : string.Equals(context.Client, AgentClient, StringComparison.Ordinal) ? SaveSource.Agent
                : SaveSource.Web;
        }

        /// <summary>Waits for a task, its failure observed and dropped: another failure stands for it.</summary>
        /// <param name="task">The task.</param>
        /// <returns>A task that completes when the task did.</returns>
        private static async Task Observed(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // The rounds' failure is the frame's; what the work made of it changes nothing.
            }
        }

        /// <summary>The refusal of a member read before the frame runs.</summary>
        /// <returns>The exception.</returns>
        private InvalidOperationException NotRunning()
        {
            return new InvalidOperationException($"The frame of {Operation} admits enlistment while its work runs.");
        }
    }
}
