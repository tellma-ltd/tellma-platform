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

        /// <summary>
        ///     The language version the expressions were validated under.
        /// </summary>
        /// <remarks>
        ///     Supplied by the host from what it stored alongside the text, not defaulted to the
        ///     current version: defaulting would stamp every recompilation with whatever version
        ///     happened to be current, which is the one value that cannot be trusted later. An
        ///     unsupported version is a caller error rather than a diagnostic — the text may be
        ///     perfectly well-formed, and what is wrong is the pairing of this engine with data it
        ///     cannot faithfully compile.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The version is one this engine does not compile.
        /// </exception>
        public required int LanguageVersion
        {
            get;
            init
            {
                if (!QueryexLanguage.IsSupported(value))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        "This engine does not compile that language version.");
                }

                field = value;
            }
        }

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
        ///     The language version this text is being validated under.
        /// </summary>
        /// <remarks>
        ///     This is where the stamp is minted: a host validating new text records the version it
        ///     passed here alongside the text, and passes that same value back every time the text
        ///     is validated or compiled again. Required rather than defaulted to the current
        ///     version, because a default would stamp stored text with whatever happened to be
        ///     current at the moment it was re-checked, which is the one value that cannot be
        ///     trusted afterwards. An unsupported version is a caller error rather than a
        ///     diagnostic — the text may be perfectly well-formed, and what is wrong is the pairing
        ///     of this engine with data it cannot faithfully compile.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The version is one this engine does not compile.
        /// </exception>
        public required int LanguageVersion
        {
            get;
            init
            {
                if (!QueryexLanguage.IsSupported(value))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        "This engine does not compile that language version.");
                }

                field = value;
            }
        }

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

        /// <summary>
        ///     The language version the text is being authored under, current by default.
        /// </summary>
        /// <remarks>
        ///     Defaulted where the two persistence-facing entry points require it, because
        ///     discovery answers a question about text being written now and its answer is never
        ///     stored. A host reopening a stored expression for editing supplies the version that
        ///     text was validated under, so the authoring aid describes it under the rules it was
        ///     written against.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     The version is one this engine does not compile.
        /// </exception>
        public int LanguageVersion
        {
            get;
            init
            {
                if (!QueryexLanguage.IsSupported(value))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        value,
                        "This engine does not compile that language version.");
                }

                field = value;
            }
        } = QueryexLanguage.Version;

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
