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
        IReadOnlyList<TypeConflict> Conflicts);

    /// <summary>One resolved path use.</summary>
    /// <param name="Segments">The resolved logical path segments.</param>
    /// <param name="Site">Where the path was written.</param>
    public sealed record PathUse(IReadOnlyList<string> Segments, QueryexTextSite Site);

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
        IReadOnlyList<QueryexDiagnostic> Diagnostics);
}
