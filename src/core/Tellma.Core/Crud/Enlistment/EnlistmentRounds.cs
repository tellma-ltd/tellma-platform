// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §13.3: an enlisted group's pre-persist stages are tasks of its host's rounds. Every
    // frame of one host (the host's own, and the frames of the groups enlisted with it, at every
    // depth) shares the host's scheduler, loader and schema map, and the rules that hold across them:
    // who takes part, which root ids are claimed, and which group a persist failure belongs to.
    /// <summary>The validation rounds one host runs, shared by every group enlisted with it at any depth.</summary>
    /// <param name="services">The scope's write services.</param>
    /// <param name="scheduler">The host's scheduler.</param>
    /// <param name="loader">The host's loader.</param>
    /// <param name="map">The tenant's schema map.</param>
    /// <param name="hostOperation">The <c>crud.operation</c> of the host, which every enlistment is counted under.</param>
    /// <param name="participants">The classes the host's frame was opened for: its stack's service, companions and components, or an open frame's method, handler or step.</param>
    /// <param name="cancellationToken">Cancels the rounds.</param>
    internal sealed class EnlistmentRounds(
        SaveOperation services,
        ValidationScheduler scheduler,
        ContextLoader loader,
        QueryexSchemaMap map,
        string hostOperation,
        IReadOnlySet<Type> participants,
        CancellationToken cancellationToken)
    {
        /// <summary>Guards the groups and the claims.</summary>
        private readonly Lock _gate = new();

        /// <summary>Every group enlisted with the host at any depth, in enlistment order.</summary>
        private readonly List<IEnlistedGroup> _groups = [];

        /// <summary>The root ids an enlistment already claimed, by the target's resource.</summary>
        private readonly HashSet<(string Resource, long Id)> _claimed = [];

        /// <summary>Whether the rounds closed: no group admits enlistment any more.</summary>
        private volatile bool _closed;

        /// <summary>Whether the enlistments were counted.</summary>
        private int _counted;

        /// <summary>The scope's write services.</summary>
        public SaveOperation Services { get; } = services;

        /// <summary>The host's scheduler.</summary>
        public ValidationScheduler Scheduler { get; } = scheduler;

        /// <summary>The host's loader.</summary>
        public ContextLoader Loader { get; } = loader;

        /// <summary>The tenant's schema map.</summary>
        public QueryexSchemaMap Map { get; } = map;

        /// <summary>Cancels the rounds.</summary>
        public CancellationToken CancellationToken { get; } = cancellationToken;

        /// <summary>Whether the rounds closed: no group admits enlistment any more.</summary>
        public bool IsClosed => _closed;

        /// <summary>Every group enlisted with the host at any depth, in enlistment order.</summary>
        public IReadOnlyList<IEnlistedGroup> Groups
        {
            get
            {
                lock (_gate)
                {
                    return [.. _groups];
                }
            }
        }

        /// <summary>Records a group, in enlistment order.</summary>
        /// <param name="group">The group.</param>
        public void Add(IEnlistedGroup group)
        {
            ArgumentNullException.ThrowIfNull(group);
            lock (_gate)
            {
                _groups.Add(group);
            }
        }

        // Spec 0015 §13.3: a participant is a class the host's frame was opened for, or one attached
        // to the stack of a group enlisted inside it. A class outside them would write another stack
        // from a frame that never asked for it.
        /// <summary>Whether a writer takes part in the host's frame.</summary>
        /// <param name="writer">The writer's class.</param>
        /// <returns>True when it does.</returns>
        public bool IsParticipant(Type writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (Attached(participants, writer))
            {
                return true;
            }

            StackRegistry registry = Services.Registry;
            return Groups.Any(group => Attached(registry.ParticipantsOf(group.Target.Resource), writer));
        }

        // Spec 0015 §13.3: one root id enlisted twice for one target in one frame would write the row
        // twice in one batch, the second write unaware of the first; the second enlistment is refused.
        /// <summary>Claims the root ids of an enlistment, refusing one another enlistment of the target claimed.</summary>
        /// <param name="target">The target stack.</param>
        /// <param name="ids">The ids above zero of the enlistment's roots.</param>
        /// <exception cref="InvalidOperationException">An id was claimed already.</exception>
        public void Claim(StackDescriptor target, IEnumerable<long> ids)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(ids);
            lock (_gate)
            {
                // --- Every id checked before any is claimed, so a refused enlistment claims nothing ---
                long[] fresh = [.. ids.Distinct()];
                foreach (long id in fresh)
                {
                    if (_claimed.Contains((target.Resource, id)))
                    {
                        throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"The row {id} of {target.Resource} was enlisted twice in the frame. Enlist each row of a target once per frame, gathering every change to it into one enlistment."));
                    }
                }

                foreach (long id in fresh)
                {
                    _ = _claimed.Add((target.Resource, id));
                }
            }
        }

        /// <summary>Runs what each group does once a round was dispatched, in enlistment order.</summary>
        /// <param name="round">The round's 1-based number.</param>
        /// <returns>A task that completes when every group's checks ran.</returns>
        public async Task AfterRoundAsync(int round)
        {
            foreach (IEnlistedGroup group in Groups)
            {
                await group.AfterRoundAsync(round).ConfigureAwait(false);
            }
        }

        /// <summary>Closes the rounds: no group admits enlistment any more. Closing twice changes nothing.</summary>
        /// <param name="complete">Whether the rounds ended well, so what follows them runs for every group, such as the value sets.</param>
        public void Close(bool complete)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            if (complete)
            {
                foreach (IEnlistedGroup group in Groups)
                {
                    group.CompleteRounds();
                }
            }
        }

        // Spec 0015 §14.2: a failure the executor attributes to a statement ordinal belongs to the
        // group that composed that statement, and is translated against the group's payload and
        // re-homed onto the host's paths; a failure of another statement is the host's.
        /// <summary>The failure translated by the group whose statement failed.</summary>
        /// <param name="failure">The persist's failure.</param>
        /// <returns>The exception to raise; null when no group composed the failing statement.</returns>
        public Exception? Translate(DataAccessException failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            foreach (IEnlistedGroup group in Groups)
            {
                if (group.Translate(failure) is Exception translated)
                {
                    return translated;
                }
            }

            return null;
        }

        // Spec 0015 §16.1: one count per enlisted write, under the host's operation, once the host's
        // outcome is known.
        /// <summary>Counts every enlisted write once, under the host's outcome.</summary>
        /// <param name="failure">What the host ended with; null when it committed.</param>
        public void Count(Exception? failure)
        {
            if (Interlocked.Exchange(ref _counted, 1) == 1)
            {
                return;
            }

            string outcome = failure is null ? CrudOutcomes.Ok
                : CrudOutcomes.Classify(failure, aborted: false) is CrudOutcomes.Validation or CrudOutcomes.Confirmation ? CrudOutcomes.Validation
                : CrudOutcomes.Error;
            foreach (IEnlistedGroup group in Groups)
            {
                Services.Metrics.CountEnlistment(hostOperation, group.Kind, outcome);
            }
        }

        /// <summary>Returns the ids every group reserved and never sent.</summary>
        public void Release()
        {
            foreach (IEnlistedGroup group in Groups)
            {
                group.Release();
            }
        }

        /// <summary>Whether a class is attached to a set, as itself or as a class derived from a member.</summary>
        /// <param name="set">The attached classes.</param>
        /// <param name="writer">The class.</param>
        /// <returns>True when it is.</returns>
        private static bool Attached(IReadOnlySet<Type> set, Type writer)
        {
            return set.Contains(writer) || set.Any(attached => attached.IsAssignableFrom(writer));
        }
    }
}
