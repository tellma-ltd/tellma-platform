// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Internal;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     The query text of a request does not compile: every diagnostic, raised before any round
    ///     trip. Maps to 400 with the problem code <c>query-invalid</c>, one <c>errors[]</c> item per
    ///     diagnostic keyed by its clause and the diagnostics verbatim for editors.
    /// </summary>
    /// <param name="diagnostics">Every diagnostic, in the order reported; at least one, none null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostics" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="diagnostics" /> is empty or holds a null entry.</exception>
    public sealed class InvalidQueryException(IReadOnlyList<QueryexDiagnostic> diagnostics)
        : TellmaException(ProblemCode, Describe(diagnostics))
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "query-invalid";

        // Member initializers run before the base constructor, so the diagnostics are validated
        // here, before the log message is rendered from them.
        /// <summary>Every diagnostic, in the order reported.</summary>
        public IReadOnlyList<QueryexDiagnostic> Diagnostics { get; } = ReadOnlyCopies.OfEntries(diagnostics, requireOne: true, nameof(diagnostics));

        /// <summary>Renders the log message: each diagnostic's code and location, never the query text.</summary>
        /// <param name="diagnostics">The diagnostics, already validated.</param>
        /// <returns>The message.</returns>
        private static string Describe(IReadOnlyList<QueryexDiagnostic> diagnostics)
        {
            string list = LogText.List(diagnostics, static diagnostic =>
                diagnostic.Location is null ? diagnostic.Code : $"{diagnostic.Code} at {diagnostic.Location}");

            return string.Create(CultureInfo.InvariantCulture, $"The query is invalid with {diagnostics.Count} diagnostic(s): {list}.");
        }
    }
}
