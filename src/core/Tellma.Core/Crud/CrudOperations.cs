// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;
using Tellma.Core.Data;

namespace Tellma.Core.Crud
{
    // The unit of work's name is spec 0015 §5.1; the instruments and the cancelled outcome are
    // §16.1 and §14.1, the stale-context re-runs among them.
    /// <summary>
    ///     The frame every stack operation runs in: it names the scope's unit of work after the first
    ///     operation the scope runs, counts every compose the guarded runner re-enters inside it by its
    ///     cause, and counts and times each operation under the outcome it ended with. Scoped.
    /// </summary>
    /// <param name="scope">The scope's data-access counters, whose unit of work this names.</param>
    /// <param name="metrics">The pipeline's instruments.</param>
    /// <param name="cancellation">The scope's data cancellation, whose abort counts as the caller's.</param>
    /// <param name="time">The clock the durations are measured on.</param>
    /// <param name="recompositions">Where the guarded runner reports each re-entered compose.</param>
    internal sealed class CrudOperations(DataAccessScope scope, CrudMetrics metrics, DataCancellation cancellation, TimeProvider time, RecomposeLog recompositions)
    {
        /// <summary>Runs one operation in the frame.</summary>
        /// <typeparam name="TResult">What the operation returns.</typeparam>
        /// <param name="resource">The stack's or the surface's resource.</param>
        /// <param name="operation">The <c>crud.operation</c> value, a <see cref="CrudOperationNames" /> constant.</param>
        /// <param name="source">The <c>crud.source</c> value of a write; null for a read.</param>
        /// <param name="lane">The <c>crud.lane</c> value.</param>
        /// <param name="body">The operation.</param>
        /// <param name="cancellationToken">The request's token, whose cancellation makes an abort the caller's.</param>
        /// <returns>What the operation returned.</returns>
        public async Task<TResult> RunAsync<TResult>(
            string resource,
            string operation,
            string? source,
            string lane,
            Func<Task<TResult>> body,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            ArgumentNullException.ThrowIfNull(body);

            // --- The unit of work is the scope's first operation; one nested in it keeps its name ---
            Name(resource, operation);

            long started = time.GetTimestamp();
            string outcome = CrudOutcomes.Ok;
            using IDisposable counting = recompositions.Count(metrics.CountStaleRerun);
            try
            {
                return await body().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                bool aborted = cancellationToken.IsCancellationRequested || cancellation.Token.IsCancellationRequested;
                outcome = CrudOutcomes.Classify(exception, aborted);
                throw;
            }
            finally
            {
                metrics.RecordOperation(operation, outcome, source, lane, time.GetElapsedTime(started).TotalSeconds);
            }
        }

        /// <summary>
        ///     Names the scope's unit of work after an operation, unless an earlier operation of the
        ///     scope named it: an API action's frame names it so, its nested operations keeping it.
        /// </summary>
        /// <param name="resource">The stack's, the surface's or the route's resource.</param>
        /// <param name="operation">The operation's name.</param>
        public void Name(string resource, string operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            if (scope.Operation.Length == 0)
            {
                scope.Operation = $"{resource}:{operation}";
            }
        }
    }
}
