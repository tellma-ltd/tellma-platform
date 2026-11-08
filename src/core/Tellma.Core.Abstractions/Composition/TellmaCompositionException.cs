// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     Every problem found by one of the two composition gates, thrown at once: from the
    ///     composition root before the host is built, or from the startup gate before the host serves
    ///     traffic. Its message carries one line per problem.
    /// </summary>
    /// <remarks>
    ///     Thrown at startup only, and never mapped to a response: a host that throws it does not
    ///     start.
    /// </remarks>
    /// <param name="problems">Every problem found, in the order found; at least one, none null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="problems" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="problems" /> is empty or holds a null entry.</exception>
    public sealed class TellmaCompositionException(IReadOnlyList<CompositionProblem> problems) : Exception(Describe(problems))
    {
        /// <summary>Every problem found, in the order found.</summary>
        public IReadOnlyList<CompositionProblem> Problems { get; } = [.. problems];

        /// <summary>Validates the problems and renders the message: a header line, then one line per problem.</summary>
        /// <param name="problems">The problems.</param>
        /// <returns>The message.</returns>
        private static string Describe(IReadOnlyList<CompositionProblem> problems)
        {
            ArgumentNullException.ThrowIfNull(problems);
            if (problems.Count == 0)
            {
                throw new ArgumentException("A composition exception carries at least one problem.", nameof(problems));
            }

            if (problems.Any(static problem => problem is null))
            {
                throw new ArgumentException("A composition exception's problems must not contain null.", nameof(problems));
            }

            // A header line, then exactly one line per problem. '\n' rather than the platform's newline
            // keeps the message identical on every operating system the host runs on.
            string header = problems.Count == 1
                ? "The Tellma composition has 1 problem:"
                : $"The Tellma composition has {problems.Count} problems:";

            return header + "\n" + string.Join("\n", problems.Select(static problem => "- " + problem.Describe()));
        }
    }
}
