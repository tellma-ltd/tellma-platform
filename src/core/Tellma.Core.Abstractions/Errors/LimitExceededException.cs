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
    /// <remarks>
    ///     <see cref="TellmaException.Arguments" /> carries <c>limit</c>, <c>actual</c> and
    ///     <c>maximum</c>, and <c>remedy</c> when the same request can run another way.
    /// </remarks>
    /// <param name="limit">The ceiling exceeded: a <see cref="LimitNames" /> value, or a feature's own code.</param>
    /// <param name="actual">The value the request carried.</param>
    /// <param name="maximum">The ceiling's value.</param>
    /// <param name="remedy">
    ///     The operation that runs the same request within its own ceiling, such as an export's
    ///     background route <c>export/start</c>; null when none does.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="limit" /> is null, empty or white space, or <paramref name="remedy" /> is
    ///     empty or white space.
    /// </exception>
    public sealed class LimitExceededException(string limit, int actual, int maximum, string? remedy = null)
        : TellmaException(
            ProblemCode,
            Describe(limit, actual, maximum, remedy),
            ArgumentsOf(limit, actual, maximum, remedy))
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "limit-exceeded";

        /// <summary>The ceiling exceeded: a <see cref="LimitNames" /> value, or a feature's own code.</summary>
        public string Limit { get; } = ValidateLimit(limit);

        /// <summary>The value the request carried.</summary>
        public int Actual { get; } = actual;

        /// <summary>The ceiling's value.</summary>
        public int Maximum { get; } = maximum;

        /// <summary>The operation that runs the same request within its own ceiling; null when none does.</summary>
        public string? Remedy { get; } = ValidateRemedy(remedy);

        /// <summary>Refuses an empty limit name.</summary>
        /// <param name="limit">The name.</param>
        /// <returns>The name.</returns>
        private static string ValidateLimit(string limit)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(limit);
            return limit;
        }

        /// <summary>Refuses a blank remedy; null stands for none.</summary>
        /// <param name="remedy">The remedy.</param>
        /// <returns>The remedy.</returns>
        private static string? ValidateRemedy(string? remedy)
        {
            if (remedy is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(remedy);
            }

            return remedy;
        }

        /// <summary>The log message: the ceiling, the value against it, and the remedy when there is one.</summary>
        /// <param name="limit">The ceiling's name.</param>
        /// <param name="actual">The value.</param>
        /// <param name="maximum">The ceiling's value.</param>
        /// <param name="remedy">The remedy; null for none.</param>
        /// <returns>The message.</returns>
        private static string Describe(string limit, int actual, int maximum, string? remedy)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"The request exceeds the limit {limit}: {actual} against a maximum of {maximum}.");
            return string.IsNullOrWhiteSpace(remedy) ? message : $"{message} Run the same request through {remedy} instead.";
        }

        /// <summary>The arguments a caller renders the refusal with: the remedy only when there is one.</summary>
        /// <param name="limit">The ceiling's name.</param>
        /// <param name="actual">The value.</param>
        /// <param name="maximum">The ceiling's value.</param>
        /// <param name="remedy">The remedy; null for none.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> ArgumentsOf(string limit, int actual, int maximum, string? remedy)
        {
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal) { ["limit"] = limit, ["actual"] = actual, ["maximum"] = maximum };
            if (!string.IsNullOrWhiteSpace(remedy))
            {
                arguments["remedy"] = remedy;
            }

            return arguments;
        }
    }
}
