// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Data
{
    // Spec 0015 §14.2: an exhausted retry and an ambiguous commit nothing could probe become
    // DependencyUnavailableException("database", 1 s), which the edge answers as a retryable 503.
    // The stack operations translate inside the pipeline; every other door through which round
    // trips are sent (the access guard's connect, the API action invoker, the blob services)
    // translates here too, so no data-layer failure reaches the edge as an unhandled error.
    /// <summary>Translates the executor's availability failures into the dependency failure every caller can act on.</summary>
    internal static class DataAccessTranslation
    {
        /// <summary>The dependency a data-access failure names.</summary>
        public const string DatabaseDependency = "database";

        /// <summary>The wait a caller is told before trying again.</summary>
        public static readonly TimeSpan DatabaseRetryAfter = TimeSpan.FromSeconds(1);

        /// <summary>The database as an unavailable dependency, worth a retry after a second.</summary>
        /// <param name="inner">The data-layer failure.</param>
        /// <returns>The exception to throw.</returns>
        public static DependencyUnavailableException Unavailable(Exception inner)
        {
            return new DependencyUnavailableException(DatabaseDependency, DatabaseRetryAfter, inner);
        }

        /// <summary>
        ///     The translation of an availability failure: an exhausted retry, or an ambiguous commit
        ///     whose probe did not find it committed. A commit the probe confirmed passes through, since
        ///     only the code that composed the batch knows how to answer for it.
        /// </summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The translated exception; null when the failure is not one of those.</returns>
        public static DependencyUnavailableException? Translate(Exception failure)
        {
            return failure switch
            {
                // Deadlocks and transient failures outlasted the executor's retries: worth a retry soon.
                DataAccessRetryExhaustedException => Unavailable(failure),

                // A batch that cannot be probed: the caller reloads to learn whether it committed.
                DataAccessAmbiguousException { Verdict: not CommitVerdict.Committed } => Unavailable(failure),
                _ => null,
            };
        }

        /// <summary>Runs steps with their availability failures translated.</summary>
        /// <typeparam name="TResult">What the steps return.</typeparam>
        /// <param name="steps">The steps.</param>
        /// <returns>What the steps returned.</returns>
        /// <exception cref="DependencyUnavailableException">A round trip exhausted its retries, or may have committed without a way to tell.</exception>
        public static async Task<TResult> RunAsync<TResult>(Func<Task<TResult>> steps)
        {
            ArgumentNullException.ThrowIfNull(steps);
            try
            {
                return await steps().ConfigureAwait(false);
            }
            catch (DataAccessException failure) when (Translate(failure) is { } translated)
            {
                throw translated;
            }
        }

        /// <summary>Runs steps that return nothing with their availability failures translated.</summary>
        /// <param name="steps">The steps.</param>
        /// <returns>A task that completes when the steps did.</returns>
        /// <exception cref="DependencyUnavailableException">A round trip exhausted its retries, or may have committed without a way to tell.</exception>
        public static async Task RunAsync(Func<Task> steps)
        {
            ArgumentNullException.ThrowIfNull(steps);
            try
            {
                await steps().ConfigureAwait(false);
            }
            catch (DataAccessException failure) when (Translate(failure) is { } translated)
            {
                throw translated;
            }
        }
    }
}
