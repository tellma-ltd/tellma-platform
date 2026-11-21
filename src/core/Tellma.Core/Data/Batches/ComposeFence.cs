// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Data.Batches
{
    // Spec 0015 §5.1: a persist's compose awaits the participants it calls and performs no I/O of its
    // own, and a validator re-run in memory on a re-entered compose answers only from the rounds
    // already run, a request for anything else raising StaleContextException instead of a round
    // trip. The fence holds both rules where every round trip starts: a batch's execution and a
    // guarded runner's call.
    /// <summary>
    ///     The ambient refusal of round trips while a compose runs: a closed fence makes every batch
    ///     execution in the flow that closed it throw the refusal it was closed with.
    /// </summary>
    internal static class ComposeFence
    {
        /// <summary>The refusal of the current flow; null while the fence is open.</summary>
        private static readonly AsyncLocal<Func<Exception>?> Refusal = new();

        /// <summary>Whether the current flow refuses round trips.</summary>
        public static bool IsClosed => Refusal.Value is not null;

        /// <summary>Closes the fence for the current flow and every flow it starts, until the returned scope is disposed.</summary>
        /// <param name="refusal">Creates the exception a refused round trip throws.</param>
        /// <returns>The scope, whose disposal restores the fence as it was.</returns>
        public static IDisposable Close(Func<Exception> refusal)
        {
            ArgumentNullException.ThrowIfNull(refusal);
            Func<Exception>? previous = Refusal.Value;
            Refusal.Value = refusal;
            return new Reopening(previous);
        }

        /// <summary>Throws the current flow's refusal when its fence is closed.</summary>
        public static void ThrowIfClosed()
        {
            if (Refusal.Value is Func<Exception> refusal)
            {
                throw refusal();
            }
        }

        /// <summary>Restores the refusal a scope replaced.</summary>
        /// <param name="previous">The refusal before the scope; null for an open fence.</param>
        private sealed class Reopening(Func<Exception>? previous) : IDisposable
        {
            /// <inheritdoc />
            public void Dispose()
            {
                Refusal.Value = previous;
            }
        }
    }
}
