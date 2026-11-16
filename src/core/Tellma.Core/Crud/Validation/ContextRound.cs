// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Crud.Validation
{
    /// <summary>
    ///     One round of a context loader: the requests pending when it began, composed onto the
    ///     round's batch beside whatever else the batch carries, then completed once the batch ran.
    /// </summary>
    /// <remarks>
    ///     A round may be composed again onto a fresh batch, as a guarded runner does when it
    ///     re-enters its compose; only the last composition's results are read.
    /// </remarks>
    internal sealed partial class ContextLoader
    {
        /// <summary>One round's requests, grouped by structural key.</summary>
        internal sealed class ContextRound
        {
            /// <summary>The loader, whose gate guards the groups' completion.</summary>
            private readonly ContextLoader _loader;

            /// <summary>The round's groups, in request order.</summary>
            private readonly LoadGroup[] _groups;

            /// <summary>The readers of the last composition, one per group; null before the first.</summary>
            private Action[]? _readers;

            /// <summary>Creates a round.</summary>
            /// <param name="loader">The loader.</param>
            /// <param name="groups">The round's groups.</param>
            internal ContextRound(ContextLoader loader, LoadGroup[] groups)
            {
                _loader = loader;
                _groups = groups;
            }

            /// <summary>Whether the round loads nothing, so it needs no statement.</summary>
            public bool IsEmpty => _groups.Length == 0;

            /// <summary>The number of loads the round's requests were merged into.</summary>
            public int Loads => _groups.Length;

            /// <summary>Appends the round's statements to a batch.</summary>
            /// <param name="batch">The composing batch.</param>
            public void Compose(IDataBatch batch)
            {
                ArgumentNullException.ThrowIfNull(batch);
                _readers = [.. _groups.Select(group => group.Compose(batch))];
            }

            /// <summary>Reads the round's results from the batch it was last composed onto, and fills every ref.</summary>
            /// <exception cref="InvalidOperationException">The round was not composed, or its batch has not run.</exception>
            public void Complete()
            {
                Action[] readers = _readers
                    ?? throw new InvalidOperationException("A context round is composed onto its batch before it is completed.");

                // --- Every result read first, so a statement that did not run fails before any ref is filled ---
                foreach (Action read in readers)
                {
                    read();
                }

                lock (_loader._gate)
                {
                    foreach (LoadGroup group in _groups)
                    {
                        group.Finish();
                    }
                }
            }
        }
    }
}
