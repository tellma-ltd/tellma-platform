// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Data.Batches
{
    // The commit probe is spec 0012 §6.5: a batch whose response was lost but whose commit the probe
    // proved surfaces as DataAccessAmbiguousException(Committed), and its commit callbacks never run,
    // because they read results the lost response never completed. The executor and ExecuteAsync keep
    // that contract; a caller that knows what to do after such a commit runs these callbacks itself.
    /// <summary>
    ///     The callbacks a batch's composers register for a commit only the commit probe proved: the
    ///     announcements that need neither the batch's results nor its outcome, which its owner runs
    ///     when the batch surfaced as committed without its response.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A commit callback reads the batch's results and outcome, which a lost response never
    ///         completes, so it cannot run then. A composer whose callback announces something it can
    ///         also announce without them (a wider audience, a value the clients fetch, a wake that
    ///         the poller tolerates when spurious) registers that announcement here as well.
    ///     </para>
    ///     <para>
    ///         Nothing runs these callbacks on its own: the executor never does. The owner of a batch
    ///         that acts on a proven commit, such as the job engine settling a completion, runs them
    ///         once, after it caught the proof and outside any transaction.
    ///     </para>
    /// </remarks>
    internal interface IBatchProvenCommit
    {
        /// <summary>Registers an announcement to make when the batch's commit is proven only by the probe.</summary>
        /// <param name="callback">The announcement; it must not read the batch's results.</param>
        /// <exception cref="InvalidOperationException">The batch already executed.</exception>
        void OnCommitProven(Action callback);
    }
}
