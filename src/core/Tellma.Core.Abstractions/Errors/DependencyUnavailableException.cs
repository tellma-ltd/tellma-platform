// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     A dependency the operation needs is unavailable, or its outcome is unknown: the database
    ///     after exhausted retries or an ambiguous commit, the identity server, the email transport.
    ///     The caller may retry. Maps to 503 with the problem code <c>dependency-unavailable</c> and a
    ///     <c>Retry-After</c> header.
    /// </summary>
    /// <remarks><see cref="TellmaException.Arguments" /> carries <c>dependency</c>.</remarks>
    /// <param name="dependency">The dependency: <c>database</c>, <c>identity</c> or <c>email</c>.</param>
    /// <param name="retryAfter">How long the caller should wait before retrying; null for the edge's default of one second.</param>
    /// <param name="inner">The failure that made the dependency unavailable, for logs; null when there is none.</param>
    /// <exception cref="ArgumentException"><paramref name="dependency" /> is null, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryAfter" /> is negative.</exception>
    public sealed class DependencyUnavailableException(string dependency, TimeSpan? retryAfter, Exception? inner)
        : TellmaException(
            ProblemCode,
            $"The {dependency} dependency is unavailable.",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["dependency"] = dependency },
            inner)
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "dependency-unavailable";

        /// <summary>The dependency: <c>database</c>, <c>identity</c> or <c>email</c>.</summary>
        public string Dependency { get; } = ValidateDependency(dependency);

        /// <summary>How long the caller should wait before retrying; null for the edge's default.</summary>
        public TimeSpan? RetryAfter { get; } = ValidateRetryAfter(retryAfter);

        /// <summary>Refuses an empty dependency name.</summary>
        /// <param name="dependency">The name.</param>
        /// <returns>The name.</returns>
        private static string ValidateDependency(string dependency)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dependency);
            return dependency;
        }

        /// <summary>Refuses a negative wait.</summary>
        /// <param name="retryAfter">The wait.</param>
        /// <returns>The wait.</returns>
        private static TimeSpan? ValidateRetryAfter(TimeSpan? retryAfter)
        {
            return retryAfter is not { } value || value >= TimeSpan.Zero
                ? retryAfter
                : throw new ArgumentOutOfRangeException(nameof(retryAfter), retryAfter, "A retry-after wait is zero or positive.");
        }
    }
}
