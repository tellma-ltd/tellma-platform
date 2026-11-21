// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>
    ///     One diagnostic a query case expects, down to where it points and what it carries.
    /// </summary>
    /// <remarks>
    ///     A code alone says something is wrong somewhere. A host builds its report from the location,
    ///     the span within that location's text and the arguments, so a case about where a problem is
    ///     reported pins all of them.
    /// </remarks>
    /// <param name="Code">The diagnostic code.</param>
    /// <param name="Location">The input the span indexes into.</param>
    /// <param name="Span">The offending range within that input's text.</param>
    /// <param name="Arguments">Every argument the diagnostic carries, by name.</param>
    public sealed record DiagnosticSite(
        string Code,
        string? Location,
        QueryexSpan Span,
        IReadOnlyDictionary<string, string> Arguments)
    {
        /// <summary>Whether a reported diagnostic is exactly this one.</summary>
        /// <param name="diagnostic">The reported diagnostic.</param>
        /// <returns>True when its code, location, span and arguments all agree.</returns>
        public bool Matches(QueryexDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);

            return diagnostic.Code == Code
                && diagnostic.Location == Location
                && diagnostic.Span == Span
                && diagnostic.Arguments.Count == Arguments.Count
                && diagnostic.Arguments.All(argument =>
                    Arguments.TryGetValue(argument.Key, out string? value) && value == argument.Value);
        }

        /// <summary>Describes the expectation, for a failing assertion.</summary>
        /// <returns>The description.</returns>
        public override string ToString()
        {
            string arguments = string.Join(
                ", ",
                Arguments.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Code}@{Location} {Span.Start}+{Span.Length} {arguments}");
        }
    }
}
