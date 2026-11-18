// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Access
{
    // Spec 0015 §16.1's stale-context counter is tagged with the mismatch that made the runner
    // re-enter a compose: the tag the prologue or the executor reported, or the guard for a 50412,
    // which names none. Only the runner knows it, and only the pipeline counts it, so the runner
    // notes it here before it re-enters.
    /// <summary>
    ///     What made the guarded runner re-enter the compose of its current call. Scoped: the batches
    ///     of one scope run one at a time.
    /// </summary>
    internal sealed class RecomposeLog
    {
        /// <summary>The cause of the last re-entry not yet taken; null when none is pending.</summary>
        private RecomposeCause? _pending;

        /// <summary>Notes the cause of the re-entry the runner is about to make.</summary>
        /// <param name="cause">The cause.</param>
        public void Note(RecomposeCause cause)
        {
            _pending = cause;
        }

        /// <summary>Takes the cause of the pending re-entry, clearing it.</summary>
        /// <returns>The cause; null when the compose was not re-entered since the last take.</returns>
        public RecomposeCause? Take()
        {
            RecomposeCause? cause = _pending;
            _pending = null;
            return cause;
        }
    }

    /// <summary>The mismatch that made the guarded runner re-enter a compose.</summary>
    internal enum RecomposeCause
    {
        /// <summary>The permissions tag moved.</summary>
        Permissions,

        /// <summary>The settings tag moved.</summary>
        Settings,

        /// <summary>A list's or an entity's tag moved under a read or a validation round.</summary>
        Entity,

        /// <summary>The persist transaction's version-tag guard refused the batch, naming no tag.</summary>
        Guard,
    }
}
