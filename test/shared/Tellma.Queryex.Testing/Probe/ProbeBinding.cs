// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Binding.Inference;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Nullity;
using Tellma.Core.Queryex.Pipeline;

namespace Tellma.Queryex.Testing.Probe
{
    /// <summary>What to bind an expression against.</summary>
    public sealed record ProbeBindOptions
    {
        /// <summary>The schema, or null to bind without one.</summary>
        public QueryexSchema? Schema { get; init; } = Testing.Schema.LedgerFixture.Schema;

        /// <summary>The logical name of the entity paths resolve from.</summary>
        public string Root { get; init; } = "Invoice";

        /// <summary>The position the expression is being compiled for.</summary>
        public QueryexMode Mode { get; init; } = QueryexMode.Value;

        /// <summary>Whether direction suffixes are accepted.</summary>
        public bool Directions { get; init; }

        /// <summary>Whether execution will have a signed-in user.</summary>
        public bool HasUser { get; init; } = true;

        /// <summary>Whether the enclosing query groups by anything.</summary>
        public bool HasGroupingKeys { get; init; }

        /// <summary>The declared parameters.</summary>
        public IReadOnlyList<QueryexParameterDeclaration> Parameters { get; init; } = [];

        /// <summary>Whether undeclared parameters are inferred rather than rejected.</summary>
        public bool Infer { get; init; }

        /// <summary>The ceilings to apply.</summary>
        public QueryexLimits Limits { get; init; } = QueryexLimits.Default;
    }

    /// <summary>One bound node, rendered.</summary>
    /// <param name="Index">This node's position in the flat list.</param>
    /// <param name="Parent">The parent's position, or -1 at an item's root.</param>
    /// <param name="Kind">The node kind.</param>
    /// <param name="Detail">The operator, name, or literal this node carries.</param>
    /// <param name="Type">The node's type.</param>
    /// <param name="Nullity">Whether the node can evaluate to an absent value.</param>
    /// <param name="Start">Where the node begins.</param>
    /// <param name="Length">How many characters it covers.</param>
    /// <param name="Children">The children's positions.</param>
    /// <param name="Path">The resolved path segments, for a path.</param>
    /// <param name="Physical">The physical column and source, for a path.</param>
    public sealed record ProbeBoundNode(
        int Index,
        int Parent,
        string Kind,
        string Detail,
        string Type,
        string Nullity,
        int Start,
        int Length,
        IReadOnlyList<int> Children,
        IReadOnlyList<string>? Path,
        string? Physical);

    /// <summary>One bound item, rendered.</summary>
    /// <param name="Type">The item's type.</param>
    /// <param name="Nullity">Whether the item can evaluate to an absent value.</param>
    /// <param name="Direction">The direction suffix, when one was written.</param>
    /// <param name="UsesAggregation">Whether the item contains an aggregation.</param>
    /// <param name="RootIndex">Where the item's root sits in the flat node list.</param>
    public sealed record ProbeBoundItem(
        string Type,
        string Nullity,
        string Direction,
        bool UsesAggregation,
        int RootIndex);

    /// <summary>The result of binding one expression list.</summary>
    /// <param name="Succeeded">Whether every stage finished without a problem.</param>
    /// <param name="Items">The bound items.</param>
    /// <param name="Nodes">Every bound node, flattened.</param>
    /// <param name="Parameters">What inference concluded, when it ran.</param>
    /// <param name="Diagnostics">Whatever problems were reported.</param>
    public sealed record ProbeBinding(
        bool Succeeded,
        IReadOnlyList<ProbeBoundItem> Items,
        IReadOnlyList<ProbeBoundNode> Nodes,
        IReadOnlyList<ParameterUse> Parameters,
        IReadOnlyList<QueryexDiagnostic> Diagnostics);

    /// <summary>Binding, rendered.</summary>
    public static partial class QueryexProbe
    {
        /// <summary>Scans, parses, binds, and annotates one expression list.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="options">What to bind it against.</param>
        /// <returns>The rendered result.</returns>
        public static ProbeBinding Bind(string text, ProbeBindOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(text);

            ProbeBindOptions effective = options ?? new ProbeBindOptions();
            DiagnosticSink sink = new();
            InferenceContext? inference = effective.Infer ? new InferenceContext() : null;

            BindingContext context = new()
            {
                Schema = effective.Schema,
                Root = effective.Schema?.FindEntity(effective.Root),
                Mode = effective.Mode,
                HasUser = effective.HasUser,
                HasGroupingKeys = effective.HasGroupingKeys,
                Parameters = Declarations(effective.Parameters),
                Inference = inference,
            };

            bool succeeded = new ExpressionCompiler(new QueryexEngineOptions()).TryCompile(
                text,
                context,
                effective.Directions,
                effective.Limits,
                sink,
                DiagnosticLocation.None,
                out _,
                out BoundExpression? bound);

            List<ProbeBoundItem> items = [];
            List<ProbeBoundNode> nodes = [];

            if (bound is not null)
            {
                foreach (BoundItem item in bound.Items)
                {
                    int root = Flatten(item.Expression, bound.Nullity, parent: -1, nodes);
                    items.Add(new ProbeBoundItem(
                        item.Expression.Type.ToString(),
                        bound.Nullity[item.Expression].ToString(),
                        item.Direction.ToString(),
                        item.Expression.ContainsAggregate,
                        root));
                }
            }

            return new ProbeBinding(
                succeeded,
                items,
                nodes,
                inference is null ? [] : [.. inference.Solve()],
                sink.Drain());
        }

        /// <summary>Turns declarations into the lookup the binder wants.</summary>
        /// <param name="declarations">The declarations.</param>
        /// <returns>The lookup.</returns>
        private static FrozenDictionary<string, ParameterSymbol> Declarations(
            IReadOnlyList<QueryexParameterDeclaration> declarations)
        {
            Dictionary<string, ParameterSymbol> symbols = new(StringComparer.OrdinalIgnoreCase);
            foreach (QueryexParameterDeclaration declaration in declarations)
            {
                symbols[declaration.Name] = new ParameterSymbol(
                    declaration.Name,
                    BoundTypes.FromPublic(declaration.Type),
                    declaration.IsNotNull ? QueryexNullity.NotNull : QueryexNullity.Nullable);
            }

            return symbols.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Adds one bound node and its descendants to the flat list.</summary>
        /// <param name="node">The node.</param>
        /// <param name="nullity">The answers the analysis produced.</param>
        /// <param name="parent">The parent's position.</param>
        /// <param name="nodes">The list being built.</param>
        /// <returns>This node's position.</returns>
        private static int Flatten(
            TypedExpr node,
            NullityMap nullity,
            int parent,
            List<ProbeBoundNode> nodes)
        {
            int index = nodes.Count;
            List<int> children = [];
            nodes.Add(new ProbeBoundNode(
                index,
                parent,
                node.Kind.ToString(),
                BoundDetail(node),
                node.Type.ToString(),
                nullity[node].ToString(),
                node.Span.Start,
                node.Span.Length,
                children,
                node is TypedPath path ? [.. path.Segments] : null,
                node is TypedPath physical ? physical.Property.Column : null));

            foreach (TypedExpr child in node.Children)
            {
                children.Add(Flatten(child, nullity, index, nodes));
            }

            return index;
        }

        /// <summary>The operator, name, or literal a bound node carries.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The detail.</returns>
        private static string BoundDetail(TypedExpr node)
        {
            return node switch
            {
                TypedLiteral literal => Convert.ToString(literal.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                TypedPath path => string.Join(".", path.Segments),
                TypedParameter parameter => parameter.Symbol.Name,
                TypedArithmetic arithmetic => arithmetic.Operator.ToString(),
                TypedComparison comparison => comparison.Operator.ToString(),
                TypedLogical logical => logical.Operator.ToString(),
                TypedIsNull absence => absence.Negated ? "IsNotNull" : "IsNull",
                TypedCall call => call.Definition.Name,
                _ => string.Empty,
            };
        }

        /// <summary>The bound arguments a call resolved to, for a signature assertion.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="options">What to bind it against.</param>
        /// <returns>The overload index the outermost call chose, or -1.</returns>
        public static int ChosenOverload(string text, ProbeBindOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(text);

            ProbeBindOptions effective = options ?? new ProbeBindOptions();
            DiagnosticSink sink = new();
            BindingContext context = new()
            {
                Schema = effective.Schema,
                Root = effective.Schema?.FindEntity(effective.Root),
                Mode = effective.Mode,
                HasUser = effective.HasUser,
                HasGroupingKeys = effective.HasGroupingKeys,
                Parameters = Declarations(effective.Parameters),
            };

            _ = new ExpressionCompiler(new QueryexEngineOptions()).TryCompile(
                text,
                context,
                effective.Directions,
                effective.Limits,
                sink,
                DiagnosticLocation.None,
                out _,
                out BoundExpression? bound);

            return bound is null || bound.Items.Length == 0
                ? -1
                : bound.Items[0].Expression is TypedCall call
                    ? call.Definition.Signatures.IndexOf(call.Signature)
                    : -1;
        }
    }
}
