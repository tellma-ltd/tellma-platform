// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>Options for compiling a whole query.</summary>
    public sealed record QueryCompilationOptions
    {
        /// <summary>The schema to bind against.</summary>
        public required QueryexSchema Schema { get; init; }

        /// <summary>The declared parameters, shared by every clause.</summary>
        public IReadOnlyList<QueryexParameterDeclaration> Parameters
        {
            get;
            init
            {
                ArgumentNullException.ThrowIfNull(value);
                field = value;
            }
        } = [];

        /// <summary>
        ///     Whether execution will have a signed-in user. Drives the current-user function, whose
        ///     result is provably absent without one — which makes a predicate comparing against it
        ///     provably false, and lets it fold away rather than be evaluated per row.
        /// </summary>
        public bool HasUser { get; init; } = true;

        /// <summary>
        ///     The zero-based position of this query among the compiled queries a host will execute
        ///     as one command.
        /// </summary>
        /// <remarks>
        ///     Namespaces every parameter and variable the engine emits, so several compiled queries
        ///     — and a host's own raw SQL alongside them — compose into a single round trip without
        ///     colliding. Raw SQL composed that way must stay out of the engine's reserved name
        ///     prefix. The ordinal is part of the compilation's identity: the same spec at the same
        ///     ordinal produces byte-identical SQL.
        /// </remarks>
        public int BatchOrdinal { get; init; }

        /// <summary>The resource ceilings for this call site.</summary>
        public QueryexLimits Limits
        {
            get;

            // Refused rather than dereferenced later. An optional option still has a contract, and a
            // null that surfaces deep inside the engine reads as an engine defect rather than as the
            // caller's mistake.
            init
            {
                ArgumentNullException.ThrowIfNull(value);
                field = value;
            }
        } = QueryexLimits.Default;
    }

    /// <summary>Options for validating an expression list without emitting SQL.</summary>
    public sealed record ValidationOptions
    {
        /// <summary>The schema to bind against.</summary>
        public required QueryexSchema Schema { get; init; }

        /// <summary>The root entity that paths resolve from. Must belong to <see cref="Schema" />.</summary>
        public required EntityDescriptor Root { get; init; }

        /// <summary>The position the expression is being validated for.</summary>
        public required QueryexMode Mode { get; init; }

        /// <summary>
        ///     Whether direction suffixes are accepted. Only meaningful for a value shape: the two
        ///     predicate modes correspond to clauses that have no ordering, so enabling directions
        ///     for one is a caller error rather than a diagnostic.
        /// </summary>
        public bool Directions { get; init; }

        /// <summary>
        ///     The declared parameters. A parameter used but not declared is a diagnostic — an
        ///     expression must not acquire a type by accident at run time.
        /// </summary>
        public IReadOnlyList<QueryexParameterDeclaration> Parameters
        {
            get;
            init
            {
                ArgumentNullException.ThrowIfNull(value);
                field = value;
            }
        } = [];

        /// <summary>Whether execution will have a signed-in user.</summary>
        public bool HasUser { get; init; } = true;

        /// <summary>
        ///     Whether the enclosing query has at least one grouping key. Drives the nullity of
        ///     aggregates, and is meaningful only on the group axis.
        /// </summary>
        /// <remarks>
        ///     A caller validating a measure without its dimensions leaves the default, which is the
        ///     conservative direction. Compiling a whole query derives the fact from the select list
        ///     instead, so a caller never has to supply it there.
        /// </remarks>
        public bool HasGroupingKeys { get; init; }

        /// <summary>The resource ceilings for this call site.</summary>
        public QueryexLimits Limits
        {
            get;

            // Refused rather than dereferenced later. An optional option still has a contract, and a
            // null that surfaces deep inside the engine reads as an engine defect rather than as the
            // caller's mistake.
            init
            {
                ArgumentNullException.ThrowIfNull(value);
                field = value;
            }
        } = QueryexLimits.Default;
    }

    /// <summary>
    ///     Options for discovery.
    /// </summary>
    /// <remarks>
    ///     All optional: discovery works on anything that lexes and parses, and reports more the
    ///     more it is given.
    /// </remarks>
    public sealed record DiscoveryOptions
    {
        /// <summary>
        ///     The schema, when available. Without it, paths are not resolved and parameter types
        ///     are not inferred.
        /// </summary>
        public QueryexSchema? Schema { get; init; }

        /// <summary>The root entity, when a schema is supplied.</summary>
        public EntityDescriptor? Root { get; init; }

        /// <summary>
        ///     The intended mode, when known, which enables mode diagnostics. Consulted when
        ///     discovering a single expression list only; discovering a whole query derives each
        ///     clause's mode itself.
        /// </summary>
        public QueryexMode? Mode { get; init; }

        /// <summary>The resource ceilings for this call site.</summary>
        public QueryexLimits Limits
        {
            get;

            // Refused rather than dereferenced later. An optional option still has a contract, and a
            // null that surfaces deep inside the engine reads as an engine defect rather than as the
            // caller's mistake.
            init
            {
                ArgumentNullException.ThrowIfNull(value);
                field = value;
            }
        } = QueryexLimits.Default;
    }
}
