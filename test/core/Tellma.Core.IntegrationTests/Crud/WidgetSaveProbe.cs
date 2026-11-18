// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     A probe of the widget's save: a validator and a persist effect that act only on a payload
    ///     holding a widget whose code a test armed, so every other save of the shared instance runs
    ///     as if neither were registered.
    /// </summary>
    public sealed class WidgetSaveProbe
    {
        /// <summary>The arms, by the code of the widget each acts on.</summary>
        private readonly ConcurrentDictionary<string, WidgetSaveArm> _arms = new(StringComparer.Ordinal);

        /// <summary>Arms the probe for the saves of a widget.</summary>
        /// <param name="code">The widget's code.</param>
        /// <param name="arm">What the probe does for it.</param>
        /// <returns>The arm, which counts what the probe saw.</returns>
        public WidgetSaveArm Arm(string code, WidgetSaveArm arm)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            ArgumentNullException.ThrowIfNull(arm);
            _arms[code] = arm;
            return arm;
        }

        /// <summary>The arm of the first widget of a payload a test armed.</summary>
        /// <param name="widgets">The payload.</param>
        /// <returns>The arm; null when the payload holds no armed widget.</returns>
        internal WidgetSaveArm? Find(IEnumerable<Widget> widgets)
        {
            foreach (Widget widget in widgets)
            {
                if (widget.Code is string code && _arms.TryGetValue(code, out WidgetSaveArm? arm))
                {
                    return arm;
                }
            }

            return null;
        }
    }

    /// <summary>What the probe does for an armed widget, and what it saw.</summary>
    public sealed class WidgetSaveArm
    {
        /// <summary>How many times the validator ran.</summary>
        private int _validations;

        /// <summary>How many times the effect contributed.</summary>
        private int _contributions;

        /// <summary>How many times the effect ran after a commit.</summary>
        private int _commits;

        /// <summary>The widgets the validator loads on each run, given the run's number from 1; none when empty.</summary>
        public Func<int, int[]> Loads { get; init; } = static _ => [];

        /// <summary>What the validator does after its loads on each run, given the run's number from 1.</summary>
        public Func<int, Task> Validated { get; init; } = static _ => Task.CompletedTask;

        /// <summary>The id of the job the effect's statement inserts into the persist; none when null.</summary>
        public int? JobId { get; init; }

        /// <summary>How many times the validator ran: once per validation, and once more per re-run in memory.</summary>
        public int Validations => Volatile.Read(ref _validations);

        /// <summary>How many times the effect contributed its statement.</summary>
        public int Contributions => Volatile.Read(ref _contributions);

        /// <summary>How many times the effect ran after a commit.</summary>
        public int Commits => Volatile.Read(ref _commits);

        /// <summary>Counts a run of the validator.</summary>
        /// <returns>The run's number, from 1.</returns>
        internal int Validate()
        {
            return Interlocked.Increment(ref _validations);
        }

        /// <summary>Counts a contribution of the effect.</summary>
        internal void Contribute()
        {
            _ = Interlocked.Increment(ref _contributions);
        }

        /// <summary>Counts a run of the effect after a commit.</summary>
        internal void Commit()
        {
            _ = Interlocked.Increment(ref _commits);
        }
    }

    /// <summary>The probe's validator: counts its runs, loads the widgets its arm names, then does what the arm says.</summary>
    /// <param name="probe">The probe.</param>
    internal sealed class WidgetSaveProbeValidator(WidgetSaveProbe probe) : IEntityValidator<Widget>
    {
        /// <inheritdoc />
        public async Task ValidateAsync(SaveContext<Widget> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (probe.Find(context.Entities) is not WidgetSaveArm arm)
            {
                return;
            }

            // --- The run's loads, riding the validation round, or answered from it on a re-run ---
            int run = arm.Validate();
            if (arm.Loads(run) is { Length: > 0 } ids)
            {
                _ = context.Loader.ByIds<Widget, int>(ids);
                await context.Loader.LoadAsync();
            }

            await arm.Validated(run);
        }
    }

    /// <summary>The probe's persist effect: counts its calls and appends the job its arm names.</summary>
    /// <param name="probe">The probe.</param>
    internal sealed class WidgetSaveProbeEffect(WidgetSaveProbe probe) : IPersistEffect<Widget>
    {
        /// <summary>The table the effect's statement writes: no stack owns it.</summary>
        private static readonly HashSet<TableName> Jobs = [new("core", "Jobs")];

        /// <inheritdoc />
        public Task ContributeAsync(PersistContext<Widget> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (probe.Find(context.Entities) is not WidgetSaveArm arm)
            {
                return Task.CompletedTask;
            }

            arm.Contribute();
            if (arm.JobId is int jobId)
            {
                // The job row stands for an effect's own statement, which commits or rolls back with
                // the save: written from a class that owns no stack, deliberately.
#pragma warning disable TELLMA0006
                _ = context.Batch.Sql(
                    $"INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt]) VALUES ({jobId}, N'fixture.probe', 'Pending', SYSUTCDATETIME(), 1, SYSUTCDATETIME());",
                    new SqlOptions(writes: Jobs));
#pragma warning restore TELLMA0006
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task AfterCommitAsync(PersistOutcome<Widget> outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            probe.Find(outcome.Entities)?.Commit();
            return Task.CompletedTask;
        }
    }
}
