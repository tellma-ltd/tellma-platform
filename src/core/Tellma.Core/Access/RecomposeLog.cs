// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Access
{
    // Spec 0015 §16.1's stale-context counter is tagged with the mismatch that made the runner
    // re-enter a compose: the tag the prologue or the executor reported, or the guard for a 50412,
    // which names none. Only the runner knows it, so it notes it here as it re-enters; the frame every
    // stack operation runs in opens the count, so every recompose of a query, a details read or a
    // save is counted once, and a batch outside any stack operation is not.
    /// <summary>
    ///     Where the guarded runner reports each re-entry of a compose, with its cause. Scoped: the
    ///     batches of one scope run one at a time.
    /// </summary>
    internal sealed class RecomposeLog
    {
        /// <summary>Counts a re-entry; null outside a counting frame.</summary>
        private Action<RecomposeCause>? _counter;

        /// <summary>Reports the cause of the re-entry the runner is about to make.</summary>
        /// <param name="cause">The cause.</param>
        public void Note(RecomposeCause cause)
        {
            _counter?.Invoke(cause);
        }

        /// <summary>Counts every re-entry reported until the frame is left; a frame nested in another keeps the outer's count.</summary>
        /// <param name="counter">Counts one re-entry.</param>
        /// <returns>The frame, which stops the count when disposed.</returns>
        public IDisposable Count(Action<RecomposeCause> counter)
        {
            ArgumentNullException.ThrowIfNull(counter);
            Action<RecomposeCause>? outer = _counter;
            _counter ??= counter;
            return new Frame(this, outer);
        }

        /// <summary>A counting frame, which restores the count it found when disposed.</summary>
        /// <param name="log">The log.</param>
        /// <param name="outer">The count the frame found.</param>
        private sealed class Frame(RecomposeLog log, Action<RecomposeCause>? outer) : IDisposable
        {
            /// <inheritdoc />
            public void Dispose()
            {
                log._counter = outer;
            }
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
