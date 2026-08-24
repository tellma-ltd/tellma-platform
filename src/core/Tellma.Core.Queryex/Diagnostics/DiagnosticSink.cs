// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Diagnostics
{
    /// <summary>
    ///     A diagnostic as it is cached: everything except the location.
    /// </summary>
    /// <remarks>
    ///     The location belongs to the call site that retrieved a cached entry, not to the entry. A
    ///     criterion shared across a filter tree is parsed and bound once, and the same failure must
    ///     be reported against whichever branch asked for it.
    /// </remarks>
    /// <param name="Code">The stable diagnostic code.</param>
    /// <param name="Span">The offending range.</param>
    /// <param name="Arguments">Named values for message composition.</param>
    internal readonly record struct CachedDiagnostic(
        string Code,
        QueryexSpan Span,
        IReadOnlyList<KeyValuePair<string, string>> Arguments)
    {
        /// <summary>Projects this onto a call site's location.</summary>
        /// <param name="location">The location to attribute it to.</param>
        /// <returns>The public diagnostic.</returns>
        internal QueryexDiagnostic Project(string? location)
        {
            return new QueryexDiagnostic(Code, Span, location, Arguments);
        }
    }

    /// <summary>
    ///     Collects the diagnostics of one compilation, deterministically.
    /// </summary>
    /// <remarks>
    ///     Compilation reports as many independent problems as the input allows — per list item, per
    ///     filter-tree leaf, per argument position — rather than stopping at the first, so a person
    ///     fixing a stored definition sees the whole picture in one pass.
    /// </remarks>
    internal sealed class DiagnosticSink
    {
        /// <summary>The recorded entries, in the order they were reported.</summary>
        private readonly List<Entry> _entries = [];

        /// <summary>Suppresses a repeat of an identical problem found by two passes.</summary>
        /// <remarks>
        ///     The arguments are part of what makes a problem identical. Two diagnostics can share a
        ///     code, a span and a location and still be about different things — one demand per
        ///     parameter, reported at the site the demand came from — and dropping the second would
        ///     leave the first standing for a parameter it does not name.
        /// </remarks>
        private readonly HashSet<(string Code, int Start, int Length, string? Location, string Arguments)> _seen = [];

        /// <summary>Whether anything has been reported.</summary>
        internal bool HasErrors => _entries.Count > 0;

        /// <summary>
        ///     How many diagnostics have been recorded so far.
        /// </summary>
        /// <remarks>
        ///     Compared before and after a clause, which is how one clause's success is told apart
        ///     from another's failure when several of them share a sink.
        /// </remarks>
        internal int Count => _entries.Count;

        /// <summary>Opens a view over this sink that attributes everything to one location.</summary>
        /// <param name="location">The location to attribute to.</param>
        /// <returns>The scope.</returns>
        internal DiagnosticScope Scope(DiagnosticLocation location)
        {
            return new DiagnosticScope(this, location);
        }

        /// <summary>Records one diagnostic.</summary>
        /// <param name="location">Where it happened.</param>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="arguments">Named values for message composition.</param>
        internal void Report(
            in DiagnosticLocation location,
            string code,
            QueryexSpan span,
            IReadOnlyList<KeyValuePair<string, string>> arguments)
        {
            if (!_seen.Add((code, span.Start, span.Length, location.Text, ArgumentKey(arguments))))
            {
                return;
            }

            _entries.Add(new Entry(location.Order, new QueryexDiagnostic(code, span, location.Text, arguments)));
        }

        /// <summary>Renders a diagnostic's arguments as one comparable string.</summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The rendering.</returns>
        /// <remarks>
        ///     Each part is written after its own length, so no name or value can be spelled in a
        ///     way that makes one pair read as two and dedupes a diagnostic that was not a repeat.
        /// </remarks>
        private static string ArgumentKey(IReadOnlyList<KeyValuePair<string, string>> arguments)
        {
            if (arguments.Count == 0)
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new();
            foreach (KeyValuePair<string, string> argument in arguments)
            {
                builder.Append(argument.Key.Length).Append(':').Append(argument.Key);
                builder.Append(argument.Value.Length).Append(':').Append(argument.Value);
            }

            return builder.ToString();
        }

        /// <summary>
        ///     Returns everything recorded, ordered by clause and then by the order it was found.
        /// </summary>
        /// <returns>The diagnostics.</returns>
        /// <remarks>
        ///     A stable sort on the clause alone: within a clause every pass walks left to right, so
        ///     insertion order is already source order, and re-sorting on the span would scramble
        ///     the relationship between a diagnostic and the one that explains it.
        /// </remarks>
        internal IReadOnlyList<QueryexDiagnostic> Drain()
        {
            List<QueryexDiagnostic> ordered = [.. _entries
                .OrderBy(static entry => entry.Order)
                .Select(static entry => entry.Diagnostic)];

            return ordered;
        }

        /// <summary>One recorded diagnostic and the clause it belongs to.</summary>
        /// <param name="Order">The clause's binding order.</param>
        /// <param name="Diagnostic">The diagnostic.</param>
        private readonly record struct Entry(int Order, QueryexDiagnostic Diagnostic);
    }

    /// <summary>
    ///     A view over a <see cref="DiagnosticSink" /> that attributes everything to one location.
    /// </summary>
    /// <remarks>
    ///     Passing one of these rather than the sink is what keeps every stage from having to know
    ///     which clause or filter-tree branch it is compiling for.
    /// </remarks>
    /// <param name="sink">The sink to record into.</param>
    /// <param name="location">The location to attribute to.</param>
    internal readonly struct DiagnosticScope(DiagnosticSink sink, DiagnosticLocation location)
    {
        /// <summary>The sink being recorded into.</summary>
        private DiagnosticSink Sink { get; } = sink;

        /// <summary>The location everything reported here is attributed to.</summary>
        internal DiagnosticLocation Location { get; } = location;

        /// <summary>Whether this scope is usable. A default-constructed scope is not.</summary>
        internal bool IsActive => Sink is not null;

        /// <summary>Reports a diagnostic that needs no arguments.</summary>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        internal void Report(string code, QueryexSpan span)
        {
            Sink.Report(Location, code, span, []);
        }

        /// <summary>Reports a diagnostic with one named argument.</summary>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="name">The argument name.</param>
        /// <param name="value">The argument value.</param>
        internal void Report(string code, QueryexSpan span, string name, string value)
        {
            Sink.Report(Location, code, span, [new KeyValuePair<string, string>(name, value)]);
        }

        /// <summary>Reports a diagnostic with several named arguments.</summary>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="arguments">The named arguments.</param>
        internal void Report(string code, QueryexSpan span, params KeyValuePair<string, string>[] arguments)
        {
            Sink.Report(Location, code, span, arguments);
        }
    }
}
