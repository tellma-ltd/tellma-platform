// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     A cached input of the request (the caller's permissions, the tenant's settings, a cached
    ///     list) moved under it twice: the platform re-ran the work once against the fresh input and
    ///     it moved again. Nothing was written. The caller may retry at once. Maps to 503 with the
    ///     problem code <c>stale-context</c> and a one-second <c>Retry-After</c>.
    /// </summary>
    /// <remarks>
    ///     <see cref="Dependencies" /> names the inputs a batch reported stale; it is empty when the
    ///     in-transaction guard refused the write, since the guard names none. The tags themselves
    ///     never reach a response.
    /// </remarks>
    /// <param name="dependencies">The stale inputs; empty when the write guard refused.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dependencies" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="dependencies" /> holds a null entry.</exception>
    public sealed class StaleContextException(IReadOnlyList<VersionTagDependency> dependencies)
        : TellmaException(ProblemCode, Describe(dependencies))
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "stale-context";

        /// <summary>The stale inputs, in the order reported; empty when the write guard refused.</summary>
        public IReadOnlyList<VersionTagDependency> Dependencies { get; } =
            ReadOnlyCopies.OfEntries(dependencies, requireOne: false, nameof(dependencies));

        /// <summary>Renders the log message.</summary>
        /// <param name="dependencies">The stale inputs.</param>
        /// <returns>The message.</returns>
        private static string Describe(IReadOnlyList<VersionTagDependency> dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            return dependencies.Count == 0
                ? "The write guard found a version tag moved after the request re-ran once."
                : $"Version tags moved again after the request re-ran once: {LogText.List(dependencies, static d => d?.Name ?? "null")}.";
        }
    }
}
