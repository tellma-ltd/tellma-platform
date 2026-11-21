// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     A request exceeds a ceiling, checked before any round trip. A value is never silently
    ///     clamped, because clamping breaks paging arithmetic. Maps to 413 with the problem code
    ///     <c>limit-exceeded</c>.
    /// </summary>
    /// <remarks><see cref="TellmaException.Arguments" /> carries <c>limit</c>, <c>actual</c> and <c>maximum</c>.</remarks>
    /// <param name="limit">The ceiling exceeded: a <see cref="LimitNames" /> value, or a feature's own code.</param>
    /// <param name="actual">The value the request carried.</param>
    /// <param name="maximum">The ceiling's value.</param>
    /// <exception cref="ArgumentException"><paramref name="limit" /> is null, empty or white space.</exception>
    public sealed class LimitExceededException(string limit, int actual, int maximum)
        : TellmaException(
            ProblemCode,
            string.Create(CultureInfo.InvariantCulture, $"The request exceeds the limit {limit}: {actual} against a maximum of {maximum}."),
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["limit"] = limit, ["actual"] = actual, ["maximum"] = maximum })
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "limit-exceeded";

        /// <summary>The ceiling exceeded: a <see cref="LimitNames" /> value, or a feature's own code.</summary>
        public string Limit { get; } = ValidateLimit(limit);

        /// <summary>The value the request carried.</summary>
        public int Actual { get; } = actual;

        /// <summary>The ceiling's value.</summary>
        public int Maximum { get; } = maximum;

        /// <summary>Refuses an empty limit name.</summary>
        /// <param name="limit">The name.</param>
        /// <returns>The name.</returns>
        private static string ValidateLimit(string limit)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(limit);
            return limit;
        }
    }
}
