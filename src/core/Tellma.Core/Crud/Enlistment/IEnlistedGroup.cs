// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §13.3: a group is the target stack's emitter, its ContributeAsync and every
    // IPersistEffect<TTarget>, with their own persist context, placed in the host's one persist batch.
    // Its pre-persist stages run as tasks of the host's rounds; its post-commit phase runs once the
    // host committed.
    /// <summary>One write enlisted with a frame: a save or a delete of a target stack's rows.</summary>
    internal interface IEnlistedGroup
    {
        /// <summary>The target stack.</summary>
        StackDescriptor Target { get; }

        /// <summary>The <c>crud.kind</c> the enlistment is counted under: <c>save</c> or <c>delete</c>.</summary>
        string Kind { get; }

        /// <summary>Where the group sits around its host's own statements.</summary>
        EnlistOrder Order { get; }

        /// <summary>The frame the target's participants see as their host, which holds the groups they enlist.</summary>
        GroupFrame Frame { get; }

        /// <summary>The tables the group writes: the target's root and child tables.</summary>
        IReadOnlySet<TableName> Tables { get; }

        /// <summary>The first-run ids of the target's rows, in payload order, which a re-run of the host's validators reuses.</summary>
        IReadOnlyList<long> Ids { get; }

        /// <summary>Whether the target's validators judged the group's rows: its service's or a component's hook ran over them.</summary>
        bool Validates { get; }

        /// <summary>Runs what the group does once a round of its host was dispatched: its checks after the round that served its loads.</summary>
        /// <param name="round">The round's 1-based number.</param>
        /// <returns>A task that completes when the checks ran.</returns>
        Task AfterRoundAsync(int round);

        /// <summary>Runs what follows the host's last round: the checks the validators' writes feed, such as the value sets.</summary>
        void CompleteRounds();

        /// <summary>Appends the group's statements to the persist batch: its emitter, then its participants, live on a first entry and replayed on a re-entry.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="first">Whether this is the batch's first composition.</param>
        /// <returns>A task that completes when the group is composed.</returns>
        ValueTask ComposeAsync(IDataBatch batch, bool first);

        /// <summary>The failure re-homed onto the host's paths, when the statement that failed is the group's.</summary>
        /// <param name="failure">The persist's failure.</param>
        /// <returns>The exception to raise; null when the failing statement is another's.</returns>
        Exception? Translate(DataAccessException failure);

        /// <summary>Runs the group's post-commit phase, each failure contained.</summary>
        /// <param name="committed">The outcome of the batch that committed; null when none answered.</param>
        /// <returns>A task that completes when every participant returned.</returns>
        Task AfterCommitAsync(BatchOutcome? committed);

        /// <summary>The persist batch's stamp, read from the group's receipt once the host committed; null for a delete or when no receipt answered.</summary>
        DateTimeOffset? Stamp { get; }

        /// <summary>Returns the ids the group reserved and never sent.</summary>
        void Release();
    }
}
