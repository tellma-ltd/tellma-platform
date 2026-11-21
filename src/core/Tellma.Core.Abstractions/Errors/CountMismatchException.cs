// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     A delete by query matched a different number of rows than the caller was shown and
    ///     expected. The count is verified inside the delete's transaction, before any row is
    ///     deleted, so nothing was deleted. Maps to 409 with the problem code <c>count-mismatch</c>.
    /// </summary>
    /// <remarks><see cref="TellmaException.Arguments" /> carries <c>expected</c> and <c>actual</c>.</remarks>
    /// <param name="expected">The count the caller expected.</param>
    /// <param name="actual">The count the query matched.</param>
    public sealed class CountMismatchException(int expected, int actual)
        : TellmaException(
            ProblemCode,
            string.Create(CultureInfo.InvariantCulture, $"The query matched {actual} row(s), not the {expected} expected; nothing was deleted."),
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["expected"] = expected, ["actual"] = actual })
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "count-mismatch";

        /// <summary>The count the caller expected.</summary>
        public int Expected { get; } = expected;

        /// <summary>The count the query matched.</summary>
        public int Actual { get; } = actual;
    }
}
