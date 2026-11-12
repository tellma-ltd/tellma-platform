// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     A span within one of a compilation's input texts, named the way diagnostics are.
    /// </summary>
    /// <param name="Location">
    ///     Null for a single expression list, or a clause-and-item path such as "Select[2]" or
    ///     "Filter.Or[1]".
    /// </param>
    /// <param name="Span">The range within that text.</param>
    public sealed record QueryexTextSite(string? Location, QueryexSpan Span);

    /// <summary>One incompatible type demand on a parameter.</summary>
    /// <param name="Type">The type this use demanded.</param>
    /// <param name="Site">Where it demanded it.</param>
    public sealed record TypeConflict(QueryexType Type, QueryexTextSite Site);

    /// <summary>
    ///     One named parameter's uses across the input.
    /// </summary>
    /// <param name="Name">The parameter name, without the leading marker.</param>
    /// <param name="Occurrences">Every occurrence's site.</param>
    /// <param name="InferredType">
    ///     The solved type when inference found exactly one; null when nothing constrained it, when
    ///     its uses conflict, or when no schema was supplied. An unconstrained parameter is not an
    ///     error — the author simply has to choose.
    /// </param>
    /// <param name="Conflicts">
    ///     When uses demand incompatible types, each demanded type with the site that demanded it.
    ///     Empty otherwise.
    /// </param>
    public sealed record ParameterUse(
        string Name,
        IReadOnlyList<QueryexTextSite> Occurrences,
        QueryexType? InferredType,
        IReadOnlyList<TypeConflict> Conflicts)
    {
        /// <summary>
        ///     The segments of the one path the parameter is compared with, read from the query's
        ///     root; null when it meets no path, or meets paths that differ.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         What lets an authoring tool offer a picker for the entity a parameter stands for.
        ///         A parameter meets a path when, bare, it is compared with a bare path by
        ///         <c>=</c>, <c>!=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> or <c>&gt;=</c>
        ///         in either operand order, is an element of an <c>in</c> list whose tested value
        ///         is a bare path, or is a key of <c>descendantOf</c> or <c>ancestorOf</c>, whose
        ///         first argument is then the path it meets. Segments are compared ignoring case.
        ///     </para>
        ///     <para>
        ///         Inside a rebase the segments start with the rebase's navigation, so they still
        ///         read from the query's root; inside a semi-join nothing is reported, since the
        ///         paths there read from another entity's rows. Null when no schema was supplied,
        ///         like the inferred type.
        ///     </para>
        /// </remarks>
        public IReadOnlyList<string>? ComparedWith { get; init; }
    }

    /// <summary>One resolved path use.</summary>
    /// <param name="Segments">The resolved logical path segments.</param>
    /// <param name="Site">Where the path was written.</param>
    public sealed record PathUse(IReadOnlyList<string> Segments, QueryexTextSite Site);

    /// <summary>How a filter conjunct compares its path or expression with its value.</summary>
    public enum FilterConjunctOperator
    {
        /// <summary>Equal to the value; with a null value, absent.</summary>
        Equal,

        /// <summary>Not equal to the value; with a null value, present.</summary>
        NotEqual,

        /// <summary>Less than the value.</summary>
        Less,

        /// <summary>Less than or equal to the value.</summary>
        LessOrEqual,

        /// <summary>Greater than the value.</summary>
        Greater,

        /// <summary>Greater than or equal to the value.</summary>
        GreaterOrEqual,

        /// <summary>The tree node the value names, or one below it.</summary>
        DescendantOf,
    }

    /// <summary>
    ///     One restriction a row-level predicate imposes at its top level: a path or an expression
    ///     compared with a literal value.
    /// </summary>
    /// <remarks>
    ///     Only an atom reached from a filter leaf through <c>and</c> alone counts, so every row the
    ///     filter admits satisfies every conjunct. The value is the literal as the CLR value of its
    ///     type — a <see cref="decimal" /> for a number, <see cref="DateOnly" /> for a date,
    ///     <see cref="DateTime" />, <see cref="DateTimeOffset" />, <see cref="Guid" />,
    ///     <see cref="string" /> or <see cref="bool" /> — and null for an absent value, which is how
    ///     <c>is null</c> and <c>is not null</c> arrive.
    /// </remarks>
    /// <param name="Site">The atom's leaf location and span.</param>
    /// <param name="Path">The compared path's segments, read from the query's root; null for an expression.</param>
    /// <param name="Expression">The compared expression's canonical text; null for a path.</param>
    /// <param name="Operator">How the path or expression is compared with the value.</param>
    /// <param name="Value">The literal, as the CLR value of its type; null for an absent value.</param>
    /// <param name="Type">The type of the compared path or expression.</param>
    public sealed record FilterConjunct(
        QueryexTextSite Site,
        IReadOnlyList<string>? Path,
        string? Expression,
        FilterConjunctOperator Operator,
        object? Value,
        QueryexType Type);

    /// <summary>
    ///     What an expression refers to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Populated as far as the input and options allow: lexical facts always, resolved paths
    ///         and inferred types only when a schema was supplied. Discovery succeeds on anything
    ///         that lexes and parses, which is what lets an authoring tool enumerate and type the
    ///         parameters of an expression that still has an unresolved property name elsewhere in
    ///         it.
    ///     </para>
    ///     <para>
    ///         Inference is for authoring only. Validation and compilation require every parameter
    ///         declared, so a stored expression can never acquire a type by accident at run time.
    ///     </para>
    /// </remarks>
    /// <param name="Parameters">Every parameter the input refers to, with its inferred type.</param>
    /// <param name="Paths">Every resolved path use.</param>
    /// <param name="Functions">The names of every function called, deduplicated.</param>
    /// <param name="UsesAggregation">True when any clause contains an aggregation.</param>
    /// <param name="Diagnostics">Whatever problems the input allowed to be reported.</param>
    public sealed record DiscoveryResult(
        IReadOnlyList<ParameterUse> Parameters,
        IReadOnlyList<PathUse> Paths,
        IReadOnlyList<string> Functions,
        bool UsesAggregation,
        IReadOnlyList<QueryexDiagnostic> Diagnostics)
    {
        /// <summary>
        ///     The row-level predicate's top-level restrictions, ordered by filter leaf and then by
        ///     where each is written; null unless they were asked for.
        /// </summary>
        /// <remarks>
        ///     Asked for through <see cref="QueryDiscoverySpec.Conjuncts" />, and found only when a
        ///     schema was supplied. An atom under <c>or</c> or <c>not</c>, in either form, a rebase
        ///     or a semi-join, one compared with a parameter, a literal <c>true</c>, and a leaf that
        ///     does not bind yield none; the group-level predicate yields none either.
        /// </remarks>
        public IReadOnlyList<FilterConjunct>? Conjuncts { get; init; }
    }
}
