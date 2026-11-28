// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     An action that calls an external system per row succeeded for some rows and failed for the
    ///     rest; what succeeded was recorded. It is the one member of the closed set that does not
    ///     abandon the operation: the pipeline still commits what the action composed, then rethrows
    ///     it unchanged. Maps to 502 with the problem code <c>partial-failure</c>, the partial results
    ///     in <c>errorDetails.results</c> and the failed rows in <c>errorDetails.failed</c>.
    /// </summary>
    /// <param name="code">The problem code: <c>partial-failure</c> at every platform raise site.</param>
    /// <param name="results">The results of the rows that succeeded, as the action would have returned them.</param>
    /// <param name="failed">The ids of the rows that failed or were not attempted, as strings.</param>
    /// <exception cref="ArgumentException"><paramref name="code" /> is empty, or <paramref name="failed" /> holds a null entry.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="results" /> or <paramref name="failed" /> is null.</exception>
    public sealed class PartialFailureException(string code, object results, IReadOnlyList<string> failed)
        : TellmaException(code, Describe(failed))
    {
        /// <summary>The results of the rows that succeeded, as the action would have returned them.</summary>
        public object Results { get; } = results ?? throw new ArgumentNullException(nameof(results));

        /// <summary>The ids of the rows that failed or were not attempted, as strings.</summary>
        public IReadOnlyList<string> Failed { get; } = ReadOnlyCopies.OfEntries(failed, requireOne: false, nameof(failed));

        /// <summary>Renders the log message.</summary>
        /// <param name="failed">The failed ids, already validated.</param>
        /// <returns>The message.</returns>
        private static string Describe(IReadOnlyList<string> failed)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"The action partially failed; {failed.Count} row(s) failed or were not attempted: {LogText.List(failed, static id => id)}.");
        }
    }
}
