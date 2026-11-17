// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Runtime.ExceptionServices;

namespace Tellma.Core.Crud.Validation
{
    // Spec 0015 §7.2: validators are plain async methods run concurrently as tasks. A validator parks
    // at LoadAsync; when every running validator has parked or completed, one dispatch serves every
    // pending request and resumes the parked ones. A task may join a round in progress (§13.3's
    // enlisted writes), and a validator awaiting anything other than LoadAsync is active, so the
    // dispatch waits for it. Exceeding MaxValidationRounds is an authoring bug.
    /// <summary>
    ///     Runs an operation's validators as concurrent tasks and dispatches their loads in shared
    ///     rounds: one dispatch each time every registered task has parked at the loader or completed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A task is identified by the asynchronous flow it runs on, so the shared loader knows which
    ///         task parked. A task that runs concurrent work of its own, each part loading, registers
    ///         each part as a task of its own: one task parks at one load at a time.
    ///     </para>
    ///     <para>
    ///         A task registered or a request made while a round is being dispatched joins the next
    ///         round. The dispatch delegate receives the round's number, so the first round can carry
    ///         the pipeline's own loads and run its checks before any validator resumes.
    ///     </para>
    /// </remarks>
    internal sealed class ValidationScheduler
    {
        /// <summary>The task the current asynchronous flow runs as; null outside every task.</summary>
        private static readonly AsyncLocal<Participant?> CurrentParticipant = new();

        /// <summary>Guards every member below.</summary>
        private readonly Lock _gate = new();

        /// <summary>Every task registered, in registration order.</summary>
        private readonly List<Participant> _participants = [];

        /// <summary>Dispatches one round, given its 1-based number.</summary>
        private readonly Func<int, CancellationToken, Task> _dispatch;

        /// <summary>The instruments; null records nothing.</summary>
        private readonly CrudMetrics? _metrics;

        /// <summary>The <c>crud.operation</c> the rounds are recorded under.</summary>
        private readonly string _operation;

        /// <summary>The operation's cancellation.</summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>The token the torn-down tasks are cancelled with: cancelled from the start.</summary>
        private static readonly CancellationToken Aborted = new(canceled: true);

        /// <summary>Set when the rounds stop early, failing every parked task and every later park; written under the gate.</summary>
        private volatile bool _aborted;

        /// <summary>Released when the round now collecting has been dispatched; parked tasks await it.</summary>
        private TaskCompletionSource _round = NewGate();

        /// <summary>Signalled when the last running task parks or completes; null while nobody waits.</summary>
        private TaskCompletionSource? _quiet;

        /// <summary>The tasks running: neither parked nor completed.</summary>
        private int _running;

        /// <summary>The tasks parked for the round now collecting.</summary>
        private readonly List<Participant> _parked = [];

        /// <summary>Whether the pipeline asked for a round with no task parked.</summary>
        private bool _requested;

        /// <summary>The rounds dispatched so far.</summary>
        private int _dispatches;

        /// <summary>The first failure that stops the rounds: a task's exception or the cancellation.</summary>
        private ExceptionDispatchInfo? _failure;

        /// <summary>Whether <see cref="RunAsync" /> has started.</summary>
        private bool _started;

        /// <summary>Whether the rounds are over: no task may register any more.</summary>
        private bool _finished;

        /// <summary>Creates a scheduler.</summary>
        /// <param name="maxRounds">The most rounds it dispatches, the first included.</param>
        /// <param name="operation">The <c>crud.operation</c> the rounds are recorded under, such as <c>save</c>.</param>
        /// <param name="dispatch">Dispatches one round, given its 1-based number.</param>
        /// <param name="metrics">The instruments; null records nothing.</param>
        /// <param name="cancellationToken">The operation's cancellation, which stops the rounds and fails every parked task.</param>
        public ValidationScheduler(
            int maxRounds, string operation, Func<int, CancellationToken, Task> dispatch, CrudMetrics? metrics = null, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRounds);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            ArgumentNullException.ThrowIfNull(dispatch);
            MaxRounds = maxRounds;
            _operation = operation;
            _dispatch = dispatch;
            _metrics = metrics;
            _cancellationToken = cancellationToken;
        }

        /// <summary>The most rounds the scheduler dispatches, the first included.</summary>
        public int MaxRounds { get; private set; }

        /// <summary>The rounds dispatched so far.</summary>
        public int Dispatches
        {
            get
            {
                lock (_gate)
                {
                    return _dispatches;
                }
            }
        }

        /// <summary>The 1-based round that a load requested now rides.</summary>
        public int Round => Dispatches + 1;

        /// <summary>Whether the current asynchronous flow runs as one of this scheduler's tasks.</summary>
        public bool IsInTask => CurrentParticipant.Value?.Scheduler == this;

        /// <summary>
        ///     Registers a task and starts it at once, up to its first await. Allowed before the rounds
        ///     run and while they run, from a task or from a dispatch; a task registered during a
        ///     dispatch joins the next round.
        /// </summary>
        /// <param name="name">The task's name, which the round-ceiling error names, such as the validator's class.</param>
        /// <param name="task">The task's body.</param>
        /// <exception cref="InvalidOperationException">The rounds are over.</exception>
        public void Run(string name, Func<Task> task)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(task);
            Participant participant = new(this, name);
            lock (_gate)
            {
                if (_finished)
                {
                    throw new InvalidOperationException(
                        $"The task '{name}' was registered after the validation rounds closed. Register validation work while the rounds run: from a validator, or before the rounds start.");
                }

                _participants.Add(participant);
                _running++;
            }

            _ = RunParticipantAsync(participant, task);
        }

        /// <summary>
        ///     Raises the ceiling by one round, before the rounds run, for a first round that only the
        ///     pipeline's own loads need: the tasks then start after it, and keep the rounds they would
        ///     have had without it.
        /// </summary>
        /// <exception cref="InvalidOperationException">The rounds have started.</exception>
        public void AddPipelineRound()
        {
            lock (_gate)
            {
                if (_started)
                {
                    throw new InvalidOperationException("The round ceiling is raised before the validation rounds start.");
                }

                MaxRounds++;
            }
        }

        /// <summary>
        ///     Asks for a round at the next quiet point even when no task parks, for loads the pipeline
        ///     requested itself; a dispatch for other reasons serves the request too.
        /// </summary>
        public void RequestRound()
        {
            lock (_gate)
            {
                _requested = true;
            }
        }

        /// <summary>
        ///     Runs the rounds until every task has completed and no round is requested, then records
        ///     the rounds dispatched.
        /// </summary>
        /// <returns>The rounds dispatched.</returns>
        /// <exception cref="InvalidOperationException">
        ///     A task asked for a round beyond <see cref="MaxRounds" />, or the rounds were already run.
        /// </exception>
        /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
        public async Task<int> RunAsync()
        {
            lock (_gate)
            {
                if (_started)
                {
                    throw new InvalidOperationException("The validation rounds run once.");
                }

                _started = true;
            }

            // The cancellation stops the rounds at once: parked tasks fail instead of waiting for a
            // task that is still active.
            await using CancellationTokenRegistration cancellation = _cancellationToken.Register(
                static state => ((ValidationScheduler)state!).Fail(new OperationCanceledException(((ValidationScheduler)state!)._cancellationToken)),
                this);

            InvalidOperationException? exceeded = await DriveAsync().ConfigureAwait(false);

            // --- Every task settles before the outcome is known: none outlives the rounds ---
            Task[] tasks;
            lock (_gate)
            {
                _finished = true;
                tasks = [.. _participants.Select(static participant => participant.Done.Task)];
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            if (exceeded is not null)
            {
                _metrics?.RecordRounds(_operation, Dispatches, exceeded: true);
                throw exceeded;
            }

            _failure?.Throw();
            _metrics?.RecordRounds(_operation, Dispatches, exceeded: false);
            return Dispatches;
        }

        /// <summary>
        ///     Parks the current task until the round now collecting has been dispatched.
        /// </summary>
        /// <returns>A task that completes when the round's loads are served.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The current flow is no task of this scheduler, its task is parked already, or the flow
        ///     outlived its task or the rounds.
        /// </exception>
        public Task ParkAsync()
        {
            Participant participant = CurrentParticipant.Value is { } current && current.Scheduler == this
                ? current
                : throw new InvalidOperationException(
                    "LoadAsync was awaited outside the validation rounds. The loader serves the validators and the other tasks the pipeline runs; a hook that only requests loads leaves the awaiting to a validator.");

            InvalidOperationException? stray = null;
            lock (_gate)
            {
                // --- A flow that outlived its task, or the rounds, would unbalance the quiet-point count
                // and park on a round nobody dispatches: it is an authoring bug, and stops the rounds ---
                if (participant.State == ParticipantState.Completed || _finished)
                {
                    stray = new InvalidOperationException(
                        $"LoadAsync was awaited by a flow that outlived the task '{participant.Name}'{(_finished ? " and the validation rounds" : string.Empty)}. Await the work a validator starts before it returns, or run it as a task of its own.");
                }
                else if (_aborted)
                {
                    return Task.FromCanceled(Aborted);
                }
                else if (participant.State == ParticipantState.Parked)
                {
                    throw new InvalidOperationException(
                        $"The task '{participant.Name}' awaited LoadAsync while already parked at it. One task parks at one load at a time; run concurrent work that loads as a task of its own.");
                }
                else
                {
                    participant.State = ParticipantState.Parked;
                    _parked.Add(participant);
                    Leave();
                    return _round.Task;
                }
            }

            // The stray flow's own caller may never observe it, so the rounds fail with it too.
            Fail(stray);
            throw stray;
        }

        /// <summary>A gate whose continuations never run inline on the thread that releases it.</summary>
        /// <returns>The gate.</returns>
        private static TaskCompletionSource NewGate()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>Runs one task as a participant, recording its failure.</summary>
        /// <param name="participant">The participant.</param>
        /// <param name="task">The task's body.</param>
        /// <returns>A task that completes, never faulted, when the body has; the participant's <see cref="Participant.Done" /> completes with it.</returns>
        private async Task RunParticipantAsync(Participant participant, Func<Task> task)
        {
            // Set inside this method, so the value flows into the body and never back to the caller.
            CurrentParticipant.Value = participant;
            try
            {
                await task().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_aborted)
            {
                // Torn down because the rounds stopped; the stopping failure is the outcome.
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
            finally
            {
                lock (_gate)
                {
                    if (participant.State == ParticipantState.Parked)
                    {
                        // Completed straight from a failed park: it no longer waits for the round.
                        _ = _parked.Remove(participant);
                    }
                    else
                    {
                        Leave();
                    }

                    participant.State = ParticipantState.Completed;
                }

                participant.Done.TrySetResult();
            }
        }

        /// <summary>Dispatches rounds at every quiet point until nothing waits for one.</summary>
        /// <returns>The round-ceiling error, or null when the rounds ended within it.</returns>
        private async Task<InvalidOperationException?> DriveAsync()
        {
            while (true)
            {
                // --- Wait until every running task has parked or completed ---
                Task? quiet = null;
                TaskCompletionSource? release = null;
                int round = 0;
                lock (_gate)
                {
                    if (_running > 0)
                    {
                        quiet = (_quiet ??= NewGate()).Task;
                    }
                    else if (_failure is not null || (_parked.Count == 0 && !_requested))
                    {
                        // A failure ends the rounds; with nothing parked and nothing requested, they
                        // are done. Closed under the same gate, so no stray flow registers or parks
                        // on a round nobody would dispatch.
                        _finished = true;
                        return null;
                    }
                    else if (_dispatches == MaxRounds)
                    {
                        InvalidOperationException exceeded = Exceeded();
                        StopParked();
                        _finished = true;
                        return exceeded;
                    }
                    else
                    {
                        // --- Close the round: its tasks count as running again, and a new round collects ---
                        release = _round;
                        _round = NewGate();
                        foreach (Participant parked in _parked)
                        {
                            parked.State = ParticipantState.Running;
                        }

                        _running += _parked.Count;
                        _parked.Clear();
                        _requested = false;
                        round = ++_dispatches;
                    }
                }

                if (release is null)
                {
                    // The round is still collecting: some task is active.
                    await quiet!.ConfigureAwait(false);
                    continue;
                }

                // --- One dispatch serves the round, then its tasks resume ---
                try
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    await _dispatch(round, _cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // The round's loads never ran, so its tasks cannot resume: they are torn down,
                    // the rounds stopped first so each sees the teardown for what it is.
                    Fail(exception);
                    lock (_gate)
                    {
                        _finished = true;
                    }

                    _ = release.TrySetCanceled(Aborted);
                    return null;
                }

                release.TrySetResult();
            }
        }

        /// <summary>Records the first failure and stops the rounds; later failures are dropped.</summary>
        /// <param name="exception">The failure.</param>
        private void Fail(Exception exception)
        {
            lock (_gate)
            {
                _failure ??= ExceptionDispatchInfo.Capture(exception);
                StopParked();
            }
        }

        /// <summary>Fails every parked task and every later park; called under the gate.</summary>
        private void StopParked()
        {
            _aborted = true;
            _ = _round.TrySetCanceled(Aborted);
        }

        /// <summary>One running task stopped running; called under the gate.</summary>
        private void Leave()
        {
            _running--;
            if (_running == 0 && _quiet is not null)
            {
                _quiet.TrySetResult();
                _quiet = null;
            }
        }

        /// <summary>The error a round beyond the ceiling raises, naming the tasks that asked for it; called under the gate.</summary>
        /// <returns>The error.</returns>
        private InvalidOperationException Exceeded()
        {
            string asking = _parked.Count == 0
                ? "the pipeline's own loads"
                : string.Join(", ", _parked.Select(static participant => $"'{participant.Name}'").Distinct(StringComparer.Ordinal));
            return new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Validation round {_dispatches + 1} was asked for by {asking}, beyond the {MaxRounds} rounds an operation may dispatch (MaxValidationRounds). Each await of LoadAsync that needs a load costs a round; request every load a step needs before awaiting LoadAsync once."));
        }

        /// <summary>What a participant is doing.</summary>
        private enum ParticipantState
        {
            /// <summary>Running, or awaiting anything other than the loader.</summary>
            Running,

            /// <summary>Parked at the loader for the round now collecting.</summary>
            Parked,

            /// <summary>Completed.</summary>
            Completed,
        }

        /// <summary>One registered task.</summary>
        /// <param name="scheduler">The scheduler it runs under.</param>
        /// <param name="name">Its name.</param>
        private sealed class Participant(ValidationScheduler scheduler, string name)
        {
            /// <summary>The scheduler it runs under.</summary>
            public ValidationScheduler Scheduler { get; } = scheduler;

            /// <summary>Its name.</summary>
            public string Name { get; } = name;

            /// <summary>What it is doing; read and written under the scheduler's gate.</summary>
            public ParticipantState State { get; set; }

            /// <summary>Completes when the task has, never faulted; created with the participant, so it is never missing.</summary>
            public TaskCompletionSource Done { get; } = NewGate();
        }
    }
}
