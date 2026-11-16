// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Validation
{
    /// <summary>
    ///     The scheduler under generated validators that request loads, await the loader, await
    ///     foreign work (a yield, a thread-pool task, a signal from another task) and register more
    ///     validators: the rounds always end, a round is never dispatched while any validator is
    ///     active, and every load requested before an await of the loader is filled when it resumes.
    /// </summary>
    public sealed class ValidationSchedulerPropertyTests : IDisposable
    {
        /// <summary>The generator's fixed seed, so a failure reproduces.</summary>
        private const int Seed = 20261115;

        /// <summary>The scenarios run, bounded so the property holds in seconds.</summary>
        private const int Scenarios = 200;

        /// <summary>The most validators a scenario starts with.</summary>
        private const int MaxValidators = 5;

        /// <summary>The most steps of one validator.</summary>
        private const int MaxSteps = 8;

        /// <summary>How deep validators register validators.</summary>
        private const int MaxDepth = 2;

        /// <summary>The composed validation model.</summary>
        private readonly ValidationRig _rig = new();

        /// <summary>What a validator does next.</summary>
        private enum StepKind
        {
            /// <summary>Requests widgets by id.</summary>
            Request,

            /// <summary>Awaits the loader.</summary>
            Load,

            /// <summary>Awaits a yield.</summary>
            Yield,

            /// <summary>Awaits work on the thread pool.</summary>
            ThreadPool,

            /// <summary>Awaits a signal another task sets.</summary>
            Signal,

            /// <summary>Registers another validator.</summary>
            Register,
        }

        /// <summary>What a validator is doing, as the validator itself reports it.</summary>
        private enum Activity
        {
            /// <summary>Running its own code.</summary>
            Running,

            /// <summary>Awaiting foreign work.</summary>
            Foreign,

            /// <summary>Awaiting the loader.</summary>
            AtLoader,

            /// <summary>Completed.</summary>
            Done,
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _rig.Dispose();
        }

        /// <summary>Every generated scenario terminates, never dispatches early, and fills every awaited load.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task Generated_validators_terminate_and_never_see_an_early_dispatch()
        {
            Random random = new(Seed);
            int rounds = 0;
            for (int scenario = 0; scenario < Scenarios; scenario++)
            {
                Step[][] programs = [.. Enumerable.Range(0, random.Next(1, MaxValidators + 1)).Select(_ => Program(random, depth: 0))];
                rounds += await RunScenarioAsync(scenario, programs);
            }

            // The generator is no trivial one: scenarios average more than one round.
            Assert.True(rounds > Scenarios, $"The scenarios dispatched {rounds} rounds in all.");
        }

        /// <summary>Generates one validator's steps.</summary>
        /// <param name="random">The generator.</param>
        /// <param name="depth">How deep the validator is registered.</param>
        /// <returns>The steps.</returns>
        private static Step[] Program(Random random, int depth)
        {
            int count = random.Next(1, MaxSteps + 1);
            var steps = new Step[count];
            for (int index = 0; index < count; index++)
            {
                // Requests and loads dominate, so most validators make several rounds.
                StepKind kind = random.Next(10) switch
                {
                    < 3 => StepKind.Request,
                    < 6 => StepKind.Load,
                    6 => StepKind.Yield,
                    7 => StepKind.ThreadPool,
                    8 => StepKind.Signal,
                    _ => depth < MaxDepth ? StepKind.Register : StepKind.Yield,
                };
                steps[index] = kind == StepKind.Request
                    ? new Step(kind, [.. Enumerable.Range(0, random.Next(1, 4)).Select(_ => random.Next(1, 30))], null)
                    : new Step(kind, [], kind == StepKind.Register ? Program(random, depth + 1) : null);
            }

            return steps;
        }

        /// <summary>Runs one scenario and checks its properties.</summary>
        /// <param name="scenario">The scenario's number, for messages.</param>
        /// <param name="programs">The first validators' steps.</param>
        /// <returns>The rounds the scenario dispatched.</returns>
        private async Task<int> RunScenarioAsync(int scenario, Step[][] programs)
        {
            // Ids divisible by 7 have no widget, so absences are cached along the way.
            BatchScript script = new BatchScript().OnQuery<VmWidget>(static statement =>
                [.. statement.KeySet<IdList>().Where(static row => row.Id % 7 != 0).Select(static row => new VmWidget { Id = row.Id, Code = "W" })]);
            Observer monitor = new();
            int loads = 0;

            // --- The dispatch checks that every validator is at the loader or done ---
            ContextLoader? loader = null;
            ValidationScheduler scheduler = new(64, "save", async (_, token) =>
            {
                monitor.CheckQuiet(scenario);
                await loader!.DispatchAsync(script.NewBatch(), token);
            }, metrics: null, TestContext.Current.CancellationToken);
            loader = new ContextLoader(_rig.Metadata, _rig.Map, scheduler);
            ContextLoader context = loader;

            // --- A validator plays its steps, reporting what it does ---
            void Register(Step[] program)
            {
                int id = monitor.Add();
                scheduler.Run(string.Create(CultureInfo.InvariantCulture, $"validator-{id}"), async () =>
                {
                    List<ContextRef<IReadOnlyDictionary<int, VmWidget>>> requested = [];
                    foreach (Step step in program)
                    {
                        switch (step.Kind)
                        {
                            case StepKind.Request:
                                requested.Add(context.ByIds<VmWidget, int>(step.Ids));
                                break;
                            case StepKind.Load:
                                _ = Interlocked.Increment(ref loads);
                                monitor.Set(id, Activity.AtLoader);
                                await context.LoadAsync();
                                monitor.Set(id, Activity.Running);
                                monitor.CheckLoaded(scenario, id, requested);
                                break;
                            case StepKind.Yield:
                                monitor.Set(id, Activity.Foreign);
                                await Task.Yield();
                                monitor.Set(id, Activity.Running);
                                break;
                            case StepKind.ThreadPool:
                                monitor.Set(id, Activity.Foreign);
                                await Task.Run(static () => Thread.SpinWait(50));
                                monitor.Set(id, Activity.Running);
                                break;
                            case StepKind.Signal:
                                monitor.Set(id, Activity.Foreign);
                                TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                                _ = Task.Run(async () =>
                                {
                                    await Task.Yield();
                                    signal.SetResult();
                                });
                                await signal.Task;
                                monitor.Set(id, Activity.Running);
                                break;
                            case StepKind.Register:
                                Register(step.Child!);
                                break;
                            default:
                                throw new InvalidOperationException($"Unknown step {step.Kind}.");
                        }
                    }

                    monitor.Set(id, Activity.Done);
                });
            }

            foreach (Step[] program in programs)
            {
                Register(program);
            }

            // --- The rounds end, every validator done, within a bound no scenario comes near ---
            int dispatched = await scheduler.RunAsync().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.Empty(monitor.Violations);
            Assert.True(monitor.AllDone, $"Scenario {scenario}: a validator did not complete.");
            Assert.InRange(dispatched, 0, Volatile.Read(ref loads));
            Assert.Equal(dispatched, script.RoundTrips);
            return dispatched;
        }

        /// <summary>One step of a validator.</summary>
        /// <param name="Kind">What it does.</param>
        /// <param name="Ids">The ids a request asks for.</param>
        /// <param name="Child">The steps of the validator it registers.</param>
        private sealed record Step(StepKind Kind, int[] Ids, Step[]? Child);

        /// <summary>What every validator of a scenario is doing, and the violations seen.</summary>
        private sealed class Observer
        {
            /// <summary>Guards the states and the violations.</summary>
            private readonly Lock _gate = new();

            /// <summary>Each validator's activity, by id.</summary>
            private readonly List<Activity> _states = [];

            /// <summary>The violations seen.</summary>
            private readonly List<string> _violations = [];

            /// <summary>The violations seen.</summary>
            public IReadOnlyList<string> Violations
            {
                get
                {
                    lock (_gate)
                    {
                        return [.. _violations];
                    }
                }
            }

            /// <summary>Whether every validator completed.</summary>
            public bool AllDone
            {
                get
                {
                    lock (_gate)
                    {
                        return _states.All(static state => state == Activity.Done);
                    }
                }
            }

            /// <summary>Adds a validator, running.</summary>
            /// <returns>Its id.</returns>
            public int Add()
            {
                lock (_gate)
                {
                    _states.Add(Activity.Running);
                    return _states.Count - 1;
                }
            }

            /// <summary>Records what a validator does now.</summary>
            /// <param name="id">The validator.</param>
            /// <param name="activity">What it does.</param>
            public void Set(int id, Activity activity)
            {
                lock (_gate)
                {
                    _states[id] = activity;
                }
            }

            /// <summary>Records a violation when a validator is active while a round is dispatched.</summary>
            /// <param name="scenario">The scenario.</param>
            public void CheckQuiet(int scenario)
            {
                lock (_gate)
                {
                    for (int id = 0; id < _states.Count; id++)
                    {
                        if (_states[id] is not (Activity.AtLoader or Activity.Done))
                        {
                            _violations.Add(string.Create(CultureInfo.InvariantCulture, $"Scenario {scenario}: a round was dispatched while validator-{id} was {_states[id]}."));
                        }
                    }
                }
            }

            /// <summary>Records a violation when a load a validator requested is not filled after its await of the loader.</summary>
            /// <param name="scenario">The scenario.</param>
            /// <param name="id">The validator.</param>
            /// <param name="requested">The loads it requested so far.</param>
            public void CheckLoaded(int scenario, int id, List<ContextRef<IReadOnlyDictionary<int, VmWidget>>> requested)
            {
                if (requested.All(static load => load.IsLoaded))
                {
                    return;
                }

                lock (_gate)
                {
                    _violations.Add(string.Create(CultureInfo.InvariantCulture, $"Scenario {scenario}: validator-{id} resumed from the loader with a load unfilled."));
                }
            }
        }
    }
}
