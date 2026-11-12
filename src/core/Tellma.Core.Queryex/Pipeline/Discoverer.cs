// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Binding.Inference;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Functions;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     Works out what an expression refers to, for a tool that is helping someone write one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Answers as much as the input allows rather than refusing everything the moment one
    ///         thing is wrong: an author with a misspelled property still wants the parameters of
    ///         the rest of the expression listed and typed.
    ///     </para>
    ///     <para>
    ///         Types are inferred here and nowhere else. Validating and compiling both require every
    ///         parameter to have been declared, so a stored expression cannot pick up a type by
    ///         accident years after it was written.
    ///     </para>
    /// </remarks>
    internal static class Discoverer
    {
        /// <summary>Discovers what one expression list refers to.</summary>
        /// <param name="compiler">The stage runner, which remembers what it has already done.</param>
        /// <param name="text">The expression text.</param>
        /// <param name="options">The discovery options.</param>
        /// <returns>What it refers to.</returns>
        internal static DiscoveryResult Discover(
            ExpressionCompiler compiler,
            string text,
            DiscoveryOptions options)
        {
            DiscoveryState state = new(compiler, options, conjuncts: false);
            state.Read(text, options.Mode ?? QueryexMode.Value, directions: false, DiagnosticLocation.None);
            return state.Result();
        }

        /// <summary>Discovers what a whole query under authoring refers to.</summary>
        /// <param name="compiler">The stage runner, which remembers what it has already done.</param>
        /// <param name="spec">The clauses written so far.</param>
        /// <param name="options">The discovery options.</param>
        /// <returns>What they refer to.</returns>
        /// <remarks>
        ///     One inference run across every clause, not one per clause. A parameter dated by the
        ///     ordering and equated to another in the filter is only solvable if both facts meet,
        ///     and no merging of separate per-clause answers can put back a link that was discarded
        ///     when each was solved on its own.
        /// </remarks>
        internal static DiscoveryResult DiscoverQuery(
            ExpressionCompiler compiler,
            QueryDiscoverySpec spec,
            DiscoveryOptions options)
        {
            DiscoveryState state = new(compiler, options, spec.Conjuncts);
            QueryexMode value = spec.Aggregate ? QueryexMode.Aggregate : QueryexMode.Value;

            if (spec.Filter is not null)
            {
                // Only the row-level predicate's top-level atoms are read as conjuncts, and only
                // when they were asked for.
                state.ReadFilter(
                    spec.Filter,
                    QueryexMode.Filter,
                    DiagnosticLocation.Clause(QueryexClause.Filter),
                    conjunctive: spec.Conjuncts);
            }

            if (spec.Select is not null)
            {
                state.Read(spec.Select, value, false, DiagnosticLocation.Clause(QueryexClause.Select));
            }

            if (spec.Having is not null)
            {
                state.ReadFilter(
                    spec.Having,
                    QueryexMode.AggregateFilter,
                    DiagnosticLocation.Clause(QueryexClause.Having),
                    conjunctive: false);
            }

            if (spec.OrderBy is not null)
            {
                state.Read(spec.OrderBy, value, true, DiagnosticLocation.Clause(QueryexClause.OrderBy));
            }

            return state.Result();
        }

        /// <summary>What the paths of one text resolve from, and how they are reported.</summary>
        /// <param name="Root">The entity the paths resolve from, or null when there is no schema.</param>
        /// <param name="Prefix">
        ///     The segments of the rebases the text sits under, which every reported path begins with
        ///     so that it stays relative to the query's root.
        /// </param>
        /// <param name="Reports">
        ///     Whether paths are reported at all. They are not inside a semi-join, whose paths resolve
        ///     from another entity's rows and are relative to nothing the query's root can reach.
        /// </param>
        private readonly record struct PathScope(
            EntityDescriptor? Root,
            ImmutableArray<string> Prefix,
            bool Reports);

        /// <summary>What one discovery run has learned so far.</summary>
        private sealed class DiscoveryState
        {
            /// <summary>The parameter name a caller error from a tree's structure is attributed to.</summary>
            private const string SpecParameter = "spec";

            /// <summary>The stage runner.</summary>
            private readonly ExpressionCompiler _compiler;

            /// <summary>The discovery options.</summary>
            private readonly DiscoveryOptions _options;

            /// <summary>Where problems are recorded.</summary>
            private readonly DiagnosticSink _sink = new();

            /// <summary>The shared inference run.</summary>
            private readonly InferenceContext _inference = new();

            /// <summary>Every resolved path use.</summary>
            private readonly List<PathUse> _paths = [];

            /// <summary>Every function name called, in the order first seen.</summary>
            private readonly List<string> _functions = [];

            /// <summary>The function names already seen.</summary>
            private readonly HashSet<string> _seenFunctions = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Which input each location text names, so a conflict can be attributed.</summary>
            private readonly Dictionary<string, DiagnosticLocation> _locations = new(StringComparer.Ordinal);

            /// <summary>
            ///     The path each parameter is compared with, by name ignoring case: null once it has
            ///     met two paths that differ.
            /// </summary>
            private readonly Dictionary<string, IReadOnlyList<string>?> _comparedWith =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>The row-level predicate's top-level restrictions, when they were asked for.</summary>
            private readonly List<FilterConjunct>? _conjuncts;

            /// <summary>Whether any clause aggregates.</summary>
            private bool _aggregates;

            /// <summary>The ceilings that span every text this run reads.</summary>
            /// <remarks>
            ///     Discovery binds as much text as a compilation does — a whole query's clauses and
            ///     a whole filter tree — so the ceiling that spans clauses has to be charged here
            ///     too. The per-text parse ceilings cannot stand in for it: one is built per text,
            ///     and it is the number of texts that discovery leaves unbounded.
            /// </remarks>
            private readonly CompilationBudget _budget;

            /// <summary>Initializes a run.</summary>
            /// <param name="compiler">The stage runner.</param>
            /// <param name="options">The discovery options.</param>
            /// <param name="conjuncts">Whether the row-level predicate's top-level restrictions are asked for.</param>
            internal DiscoveryState(ExpressionCompiler compiler, DiscoveryOptions options, bool conjuncts)
            {
                _compiler = compiler;
                _options = options;
                _budget = new CompilationBudget(options.Limits);
                _conjuncts = conjuncts ? [] : null;
            }

            /// <summary>Reads one expression list.</summary>
            /// <param name="text">The expression text.</param>
            /// <param name="mode">The position it is written for.</param>
            /// <param name="directions">Whether direction suffixes are accepted here.</param>
            /// <param name="location">Which input this text is.</param>
            /// <param name="queryexVersion">
            ///     The version to bind under, when the text carries a stamp of its own; otherwise the
            ///     one the options name.
            /// </param>
            internal void Read(
                string text,
                QueryexMode mode,
                bool directions,
                DiagnosticLocation location,
                int? queryexVersion = null)
            {
                Read(text, mode, directions, location, queryexVersion, Top(), conjunctive: false);
            }

            /// <summary>Reads one expression list, resolving its paths from a given scope.</summary>
            /// <param name="text">The expression text.</param>
            /// <param name="mode">The position it is written for.</param>
            /// <param name="directions">Whether direction suffixes are accepted here.</param>
            /// <param name="location">Which input this text is.</param>
            /// <param name="queryexVersion">The version stamped on the text, when it has one.</param>
            /// <param name="scope">What its paths resolve from, and how they are reported.</param>
            /// <param name="conjunctive">
            ///     Whether the text is a filter leaf reached through conjunctions alone, whose
            ///     top-level restrictions are to be read.
            /// </param>
            private void Read(
                string text,
                QueryexMode mode,
                bool directions,
                DiagnosticLocation location,
                int? queryexVersion,
                PathScope scope,
                bool conjunctive)
            {
                if (_budget.Exceeded)
                {
                    // The ceiling is blown and has already said so; reading further texts would only
                    // add to a total that is over.
                    return;
                }

                _locations[location.Text ?? string.Empty] = location;

                if (!_compiler.TryParse(
                    text,
                    _options.Limits,
                    _sink,
                    location,
                    out ExpressionListSyntax? syntax)
                    || syntax is null)
                {
                    return;
                }

                ReadSyntax(syntax);

                // Counted off the parse, so each parameter is reported once per place it is written
                // whether or not a schema was supplied and whatever binding does with it.
                ReadParameters(syntax, location);

                if (_options.Schema is null || scope.Root is null)
                {
                    // Without a schema there is nothing to resolve a name against and nothing to
                    // constrain a parameter with, so what the grammar alone shows is the answer.
                    return;
                }

                BindingContext context = new()
                {
                    Schema = _options.Schema,
                    Root = scope.Root,
                    Mode = mode,
                    QueryexVersion = queryexVersion ?? _options.QueryexVersion,
                    Inference = _inference,
                };

                BoundExpression bound = _compiler.Bind(
                    text,
                    syntax,
                    context,
                    directions,
                    _options.Limits,
                    _sink,
                    location);

                _budget.TryConsumeTypedNodes(bound.NodeCount, default, _sink.Scope(location));

                foreach (BoundItem item in bound.Items)
                {
                    ReadBound(item.Expression, location, scope);
                }

                // A predicate is one item; a leaf that is a list is a mistake binding has already
                // reported, and describes no restriction.
                if (conjunctive && _conjuncts is not null && bound.Items.Length == 1)
                {
                    ConjunctReader.Read(bound.Items[0].Expression, syntax, location.Text, _conjuncts);
                }
            }

            /// <summary>Reads a whole filter tree.</summary>
            /// <param name="tree">The tree.</param>
            /// <param name="mode">The position its leaves are written for.</param>
            /// <param name="location">Which clause the tree belongs to.</param>
            /// <param name="conjunctive">Whether the top-level restrictions of its leaves are to be read.</param>
            internal void ReadFilter(FilterTree tree, QueryexMode mode, DiagnosticLocation location, bool conjunctive)
            {
                ReadFilter(tree, mode, location, Top(), conjunctive);
            }

            /// <summary>Reads one node of a filter tree, resolving its paths from a given scope.</summary>
            /// <param name="tree">The node.</param>
            /// <param name="mode">The position its leaves are written for.</param>
            /// <param name="location">Which input the node is.</param>
            /// <param name="scope">What its paths resolve from, and how they are reported.</param>
            /// <param name="conjunctive">
            ///     Whether the node is reached through conjunctions alone, and its leaves' top-level
            ///     restrictions are to be read.
            /// </param>
            private void ReadFilter(
                FilterTree tree,
                QueryexMode mode,
                DiagnosticLocation location,
                PathScope scope,
                bool conjunctive)
            {
                // Only a conjunction passes the reading of restrictions down: below a negation or a
                // disjunction an atom no longer restricts every row, and below a rebase or a
                // semi-join it restricts other rows than the query's.
                switch (tree)
                {
                    case FilterTree.LeafNode leaf:
                        Read(leaf.Text, mode, false, location, leaf.QueryexVersion, scope, conjunctive);
                        return;

                    case FilterTree.NotNode negation:
                        ReadFilter(negation.Operand, mode, location.Child(FilterConnective.Not, 0), scope, false);
                        return;

                    case FilterTree.AndNode conjunction:
                        ReadChildren(conjunction.Children, mode, location, FilterConnective.And, scope, conjunctive);
                        return;

                    case FilterTree.OrNode disjunction:
                        ReadChildren(disjunction.Children, mode, location, FilterConnective.Or, scope, false);
                        return;

                    case FilterTree.ViaNode via:
                        ReadFilter(
                            via.Inner,
                            mode,
                            location.Via(via.Navigation),
                            Rebased(via, location, scope),
                            false);
                        return;

                    case FilterTree.ExistsNode exists:
                        ReadFilter(
                            exists.Inner,
                            mode,
                            location.Exists(exists.Entity),
                            Searched(exists, location, scope),
                            false);
                        return;

                    default:
                        return;
                }
            }

            /// <summary>The scope of the query's own clauses.</summary>
            /// <returns>The scope.</returns>
            private PathScope Top()
            {
                return new PathScope(_options.Root, [], Reports: true);
            }

            /// <summary>The scope of a rebased criterion.</summary>
            /// <param name="via">The rebase.</param>
            /// <param name="location">Where it sits.</param>
            /// <param name="scope">The scope it sits in.</param>
            /// <returns>The scope its criterion reads from.</returns>
            /// <remarks>
            ///     The criterion's paths resolve from the navigation's target and are reported with the
            ///     navigation's segments in front, so every reported path still reads from the query's
            ///     root.
            /// </remarks>
            private PathScope Rebased(FilterTree.ViaNode via, DiagnosticLocation location, PathScope scope)
            {
                if (_options.Schema is null || scope.Root is null)
                {
                    return scope;
                }

                ResolvedVia resolved = FilterStructure.ResolveVia(scope.Root, via, location, SpecParameter);
                return scope with { Root = resolved.Target, Prefix = [.. scope.Prefix, .. resolved.Segments] };
            }

            /// <summary>The scope of a semi-join's criterion.</summary>
            /// <param name="exists">The semi-join.</param>
            /// <param name="location">Where it sits.</param>
            /// <param name="scope">The scope it sits in.</param>
            /// <returns>The scope its criterion reads from.</returns>
            /// <remarks>
            ///     The criterion is still bound, so its problems and its parameters are reported like
            ///     any other leaf's. Its paths are not: they resolve from the searched entity's rows,
            ///     and no prefix would make them read from the query's root.
            /// </remarks>
            private PathScope Searched(FilterTree.ExistsNode exists, DiagnosticLocation location, PathScope scope)
            {
                if (_options.Schema is null || scope.Root is null)
                {
                    return scope with { Reports = false };
                }

                ResolvedExists resolved = FilterStructure.ResolveExists(
                    _options.Schema,
                    scope.Root,
                    exists,
                    location,
                    SpecParameter);

                return new PathScope(resolved.Entity, [], Reports: false);
            }

            /// <summary>Collects everything learned into a result.</summary>
            /// <returns>The result.</returns>
            internal DiscoveryResult Result()
            {
                List<ParameterUse> parameters = [.. _inference.Solve()];
                ReportConflicts(parameters);

                // The one path each parameter met, where it met exactly one.
                for (int index = 0; index < parameters.Count; index++)
                {
                    if (_comparedWith.GetValueOrDefault(parameters[index].Name) is IReadOnlyList<string> path)
                    {
                        parameters[index] = parameters[index] with { ComparedWith = path };
                    }
                }

                return new DiscoveryResult(
                    parameters,
                    _paths,
                    _functions,
                    _aggregates,
                    _sink.Drain())
                {
                    Conjuncts = _conjuncts,
                };
            }

            /// <summary>Reports every parameter whose uses cannot all be satisfied at once.</summary>
            /// <param name="parameters">The solved parameters.</param>
            /// <remarks>
            ///     Reported once per demand rather than once per parameter, because what an author
            ///     needs to see is the two places that disagree, not that they disagree.
            /// </remarks>
            private void ReportConflicts(List<ParameterUse> parameters)
            {
                foreach (ParameterUse use in parameters)
                {
                    foreach (TypeConflict conflict in use.Conflicts)
                    {
                        DiagnosticLocation at = _locations.GetValueOrDefault(
                            conflict.Site.Location ?? string.Empty,
                            DiagnosticLocation.None);

                        _sink.Report(
                            at,
                            DiagnosticCodes.ConflictingParameterTypes,
                            conflict.Site.Span,
                            [
                                new KeyValuePair<string, string>(DiagnosticArgumentNames.Name, use.Name),
                                new KeyValuePair<string, string>(
                                    DiagnosticArgumentNames.Type,
                                    conflict.Type.ToString()),
                            ]);
                    }
                }
            }

            /// <summary>Reads every child of a connective.</summary>
            /// <param name="children">The children.</param>
            /// <param name="mode">The position their leaves are written for.</param>
            /// <param name="location">Which clause the tree belongs to.</param>
            /// <param name="connective">The connective, as a location component.</param>
            /// <param name="scope">What their paths resolve from, and how they are reported.</param>
            /// <param name="conjunctive">Whether their leaves' top-level restrictions are to be read.</param>
            private void ReadChildren(
                IReadOnlyList<FilterTree> children,
                QueryexMode mode,
                DiagnosticLocation location,
                FilterConnective connective,
                PathScope scope,
                bool conjunctive)
            {
                for (int index = 0; index < children.Count; index++)
                {
                    ReadFilter(children[index], mode, location.Child(connective, index), scope, conjunctive);
                }
            }

            /// <summary>Collects the facts the grammar alone settles.</summary>
            /// <param name="node">The parsed node.</param>
            private void ReadSyntax(SyntaxNode node)
            {
                if (node is CallSyntax call)
                {
                    if (_seenFunctions.Add(call.Name))
                    {
                        _functions.Add(call.Name);
                    }

                    _aggregates |= FunctionRegistry.IsAggregate(call.Name);
                }

                foreach (SyntaxNode child in node.Children)
                {
                    ReadSyntax(child);
                }
            }

            /// <summary>Records where each parameter was written, without binding anything.</summary>
            /// <param name="node">The parsed node.</param>
            /// <param name="location">Which input this text is.</param>
            private void ReadParameters(SyntaxNode node, DiagnosticLocation location)
            {
                if (node is ParameterSyntax parameter)
                {
                    _inference
                        .GetOrCreate(parameter.Name)
                        .Occur(new QueryexTextSite(location.Text, parameter.Span));
                }

                foreach (SyntaxNode child in node.Children)
                {
                    ReadParameters(child, location);
                }
            }

            /// <summary>Collects the facts only binding settles.</summary>
            /// <param name="node">The bound node.</param>
            /// <param name="location">Which input this text is.</param>
            /// <param name="scope">What the text's paths resolve from, and how they are reported.</param>
            private void ReadBound(TypedExpr node, DiagnosticLocation location, PathScope scope)
            {
                if (node is TypedPath path && scope.Reports)
                {
                    _paths.Add(new PathUse(Rooted(path, scope), new QueryexTextSite(location.Text, path.Span)));
                }

                if (scope.Reports)
                {
                    ReadComparedWith(node, scope);
                }

                foreach (TypedExpr child in node.Children)
                {
                    ReadBound(child, location, scope);
                }
            }

            /// <summary>Notes the path a bare parameter meets in one node, when it meets one.</summary>
            /// <param name="node">The bound node.</param>
            /// <param name="scope">What the text's paths resolve from.</param>
            /// <remarks>
            ///     A parameter meets a path when, bare, it is compared with a bare path, is an element
            ///     of a membership list whose tested value is a bare path, or is a key of a hierarchy
            ///     predicate, whose first argument is the path.
            /// </remarks>
            private void ReadComparedWith(TypedExpr node, PathScope scope)
            {
                switch (node)
                {
                    case TypedComparison { Left: TypedParameter parameter, Right: TypedPath path }:
                        Meet(parameter, path, scope);
                        return;

                    case TypedComparison { Left: TypedPath path, Right: TypedParameter parameter }:
                        Meet(parameter, path, scope);
                        return;

                    case TypedIn { Value: TypedPath path } membership:
                        foreach (TypedExpr element in membership.Elements)
                        {
                            if (element is TypedParameter parameter)
                            {
                                Meet(parameter, path, scope);
                            }
                        }

                        return;

                    case TypedCall call when IsHierarchyPredicate(call) && call.Arguments[0] is TypedPath path:
                        for (int index = 1; index < call.Arguments.Length; index++)
                        {
                            if (call.Arguments[index] is TypedParameter parameter)
                            {
                                Meet(parameter, path, scope);
                            }
                        }

                        return;

                    default:
                        return;
                }
            }

            /// <summary>Records that a parameter meets a path.</summary>
            /// <param name="parameter">The parameter.</param>
            /// <param name="path">The path.</param>
            /// <param name="scope">What the path resolves from.</param>
            /// <remarks>
            ///     A parameter that meets two paths whose segments differ, ignoring case, is compared
            ///     with no one path, and stays that way whatever else it meets.
            /// </remarks>
            private void Meet(TypedParameter parameter, TypedPath path, PathScope scope)
            {
                IReadOnlyList<string> segments = Rooted(path, scope);
                if (!_comparedWith.TryGetValue(parameter.Symbol.Name, out IReadOnlyList<string>? met))
                {
                    _comparedWith[parameter.Symbol.Name] = segments;
                    return;
                }

                if (met is not null && !met.SequenceEqual(segments, StringComparer.OrdinalIgnoreCase))
                {
                    _comparedWith[parameter.Symbol.Name] = null;
                }
            }

            /// <summary>A path's segments, read from the query's root.</summary>
            /// <param name="path">The path.</param>
            /// <param name="scope">What the path resolves from.</param>
            /// <returns>The segments, behind those of the rebases the text sits under.</returns>
            private static ImmutableArray<string> Rooted(TypedPath path, PathScope scope)
            {
                return scope.Prefix.IsEmpty
                    ? path.Segments
                    : [.. scope.Prefix, .. path.Segments];
            }

            /// <summary>Whether a call is one of the two hierarchy predicates, with a key path and keys.</summary>
            /// <param name="call">The bound call.</param>
            /// <returns>True when it is.</returns>
            private static bool IsHierarchyPredicate(TypedCall call)
            {
                return call.Arguments.Length > 1
                    && (call.Definition.Name.Equals("descendantOf", StringComparison.OrdinalIgnoreCase)
                        || call.Definition.Name.Equals("ancestorOf", StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
