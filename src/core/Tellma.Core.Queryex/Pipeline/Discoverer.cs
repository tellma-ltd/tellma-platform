// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

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
            DiscoveryState state = new(compiler, options);
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
            DiscoveryState state = new(compiler, options);
            QueryexMode value = spec.Aggregate ? QueryexMode.Aggregate : QueryexMode.Value;

            if (spec.Filter is not null)
            {
                state.ReadFilter(
                    spec.Filter,
                    spec.Aggregate ? QueryexMode.Filter : QueryexMode.Filter,
                    DiagnosticLocation.Clause(QueryexClause.Filter));
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
                    DiagnosticLocation.Clause(QueryexClause.Having));
            }

            if (spec.OrderBy is not null)
            {
                state.Read(spec.OrderBy, value, true, DiagnosticLocation.Clause(QueryexClause.OrderBy));
            }

            return state.Result();
        }

        /// <summary>What one discovery run has learned so far.</summary>
        private sealed class DiscoveryState
        {
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
            internal DiscoveryState(ExpressionCompiler compiler, DiscoveryOptions options)
            {
                _compiler = compiler;
                _options = options;
                _budget = new CompilationBudget(options.Limits);
            }

            /// <summary>Reads one expression list.</summary>
            /// <param name="text">The expression text.</param>
            /// <param name="mode">The position it is written for.</param>
            /// <param name="directions">Whether direction suffixes are accepted here.</param>
            /// <param name="location">Which input this text is.</param>
            internal void Read(string text, QueryexMode mode, bool directions, DiagnosticLocation location)
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

                if (_options.Schema is null || _options.Root is null)
                {
                    // Without a schema there is nothing to resolve a name against and nothing to
                    // constrain a parameter with, so what the grammar alone shows is the answer.
                    return;
                }

                BindingContext context = new()
                {
                    Schema = _options.Schema,
                    Root = _options.Root,
                    Mode = mode,
                    LanguageVersion = _options.LanguageVersion,
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
                    ReadBound(item.Expression, location);
                }
            }

            /// <summary>Reads a whole filter tree.</summary>
            /// <param name="tree">The tree.</param>
            /// <param name="mode">The position its leaves are written for.</param>
            /// <param name="location">Which clause the tree belongs to.</param>
            internal void ReadFilter(FilterTree tree, QueryexMode mode, DiagnosticLocation location)
            {
                switch (tree)
                {
                    case FilterTree.LeafNode leaf:
                        Read(leaf.Text, mode, false, location);
                        return;

                    case FilterTree.NotNode negation:
                        ReadFilter(negation.Operand, mode, location.Child(FilterConnective.Not, 0));
                        return;

                    case FilterTree.AndNode conjunction:
                        ReadChildren(conjunction.Children, mode, location, FilterConnective.And);
                        return;

                    case FilterTree.OrNode disjunction:
                        ReadChildren(disjunction.Children, mode, location, FilterConnective.Or);
                        return;

                    default:
                        return;
                }
            }

            /// <summary>Collects everything learned into a result.</summary>
            /// <returns>The result.</returns>
            internal DiscoveryResult Result()
            {
                List<ParameterUse> parameters = [.. _inference.Solve()];
                ReportConflicts(parameters);

                return new DiscoveryResult(
                    parameters,
                    _paths,
                    _functions,
                    _aggregates,
                    _sink.Drain());
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
            private void ReadChildren(
                IReadOnlyList<FilterTree> children,
                QueryexMode mode,
                DiagnosticLocation location,
                FilterConnective connective)
            {
                for (int index = 0; index < children.Count; index++)
                {
                    ReadFilter(children[index], mode, location.Child(connective, index));
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
            private void ReadBound(TypedExpr node, DiagnosticLocation location)
            {
                if (node is TypedPath path)
                {
                    _paths.Add(new PathUse(path.Segments, new QueryexTextSite(location.Text, path.Span)));
                }

                foreach (TypedExpr child in node.Children)
                {
                    ReadBound(child, location);
                }
            }
        }
    }
}
