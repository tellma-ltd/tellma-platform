// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;

namespace Tellma.Core.Queryex.Syntax
{
    /// <summary>The kind of one syntax node.</summary>
    internal enum SyntaxKind
    {
        /// <summary>A comma-separated list of items.</summary>
        ExpressionList,

        /// <summary>One item of a list, with its optional direction suffix.</summary>
        ExpressionItem,

        /// <summary>A numeric literal.</summary>
        Number,

        /// <summary>A string literal.</summary>
        String,

        /// <summary>A boolean literal.</summary>
        Boolean,

        /// <summary>The absent-value literal.</summary>
        Null,

        /// <summary>A named parameter.</summary>
        Parameter,

        /// <summary>A dotted path.</summary>
        Path,

        /// <summary>A function call.</summary>
        Call,

        /// <summary>A prefix operator application.</summary>
        Unary,

        /// <summary>An infix operator application.</summary>
        Binary,

        /// <summary>A set-membership test.</summary>
        In,

        /// <summary>An absence test.</summary>
        IsNull,

        /// <summary>A parenthesised expression.</summary>
        Parenthesized,

        /// <summary>A region the parser could not read.</summary>
        Error,
    }

    /// <summary>A prefix operator.</summary>
    internal enum UnaryOperatorKind
    {
        /// <summary>Arithmetic negation.</summary>
        Negate,

        /// <summary>Logical negation.</summary>
        Not,
    }

    /// <summary>An infix operator.</summary>
    internal enum BinaryOperatorKind
    {
        /// <summary>Addition. Never string concatenation.</summary>
        Add,

        /// <summary>Subtraction.</summary>
        Subtract,

        /// <summary>Multiplication.</summary>
        Multiply,

        /// <summary>Division.</summary>
        Divide,

        /// <summary>Remainder.</summary>
        Remainder,

        /// <summary>String concatenation.</summary>
        Concat,

        /// <summary>Equality.</summary>
        Equal,

        /// <summary>Inequality.</summary>
        NotEqual,

        /// <summary>Ordering: strictly less.</summary>
        Less,

        /// <summary>Ordering: less or equal.</summary>
        LessOrEqual,

        /// <summary>Ordering: strictly greater.</summary>
        Greater,

        /// <summary>Ordering: greater or equal.</summary>
        GreaterOrEqual,

        /// <summary>Conjunction.</summary>
        And,

        /// <summary>Disjunction.</summary>
        Or,
    }

    /// <summary>
    ///     One node of an untyped, schema-free parse tree.
    /// </summary>
    /// <remarks>
    ///     A class rather than a record, and compared by reference rather than by value. The binder
    ///     memoizes on node identity, and two structurally identical subtrees at different positions
    ///     have to stay distinct — sharing a memo entry between them would attach one node's span to
    ///     the other node's diagnostic.
    /// </remarks>
    internal abstract class SyntaxNode
    {
        /// <summary>Initializes the node with the facts folded up from its children.</summary>
        /// <param name="kind">The node kind.</param>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="depth">The height of this subtree.</param>
        /// <param name="nodeCount">The nodes in this subtree, this one included.</param>
        /// <param name="structuralHash">A span-insensitive hash of this subtree.</param>
        private protected SyntaxNode(
            SyntaxKind kind,
            QueryexSpan span,
            int depth,
            int nodeCount,
            int structuralHash)
        {
            Kind = kind;
            Span = span;
            Depth = depth;
            NodeCount = nodeCount;
            StructuralHash = structuralHash;
        }

        /// <summary>The node kind.</summary>
        internal SyntaxKind Kind { get; }

        /// <summary>The node's range in the input.</summary>
        internal QueryexSpan Span { get; }

        /// <summary>The height of this subtree.</summary>
        internal int Depth { get; }

        /// <summary>The nodes in this subtree, this one included.</summary>
        internal int NodeCount { get; }

        /// <summary>
        ///     A hash of this subtree that ignores spans and sees through parentheses.
        /// </summary>
        /// <remarks>
        ///     Computed eagerly from the children's, which is constant work per node and needs no
        ///     lazy initialization — these trees are shared across threads through the cache, and a
        ///     lazily filled field would need a memory barrier to be safe. Suitable for bucketing
        ///     and for rejecting inequality, and never for ordering: the underlying hash is seeded
        ///     per process, so sorting by it would make output vary between runs.
        /// </remarks>
        internal int StructuralHash { get; }

        /// <summary>This node's children, in source order.</summary>
        internal abstract IReadOnlyList<SyntaxNode> Children { get; }

        /// <summary>The height of the tallest of the given subtrees, plus one.</summary>
        /// <param name="children">The children.</param>
        /// <returns>This node's depth.</returns>
        private protected static int DepthOf(IReadOnlyList<SyntaxNode> children)
        {
            int deepest = 0;
            for (int index = 0; index < children.Count; index++)
            {
                deepest = Math.Max(deepest, children[index].Depth);
            }

            return deepest + 1;
        }

        /// <summary>The total node count of the given subtrees, plus one.</summary>
        /// <param name="children">The children.</param>
        /// <returns>This node's subtree size.</returns>
        private protected static int CountOf(IReadOnlyList<SyntaxNode> children)
        {
            int total = 1;
            for (int index = 0; index < children.Count; index++)
            {
                total += children[index].NodeCount;
            }

            return total;
        }

        /// <summary>Folds the children's structural hashes into one, with the node's own kind.</summary>
        /// <param name="kind">The node kind.</param>
        /// <param name="discriminator">A per-kind discriminator, such as an operator.</param>
        /// <param name="children">The children.</param>
        /// <returns>This node's structural hash.</returns>
        private protected static int HashOf(SyntaxKind kind, int discriminator, IReadOnlyList<SyntaxNode> children)
        {
            HashCode hash = default;
            hash.Add((int)kind);
            hash.Add(discriminator);
            for (int index = 0; index < children.Count; index++)
            {
                hash.Add(children[index].StructuralHash);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>A leaf node, which has no children.</summary>
    internal abstract class SyntaxLeaf : SyntaxNode
    {
        /// <summary>Initializes the leaf.</summary>
        /// <param name="kind">The node kind.</param>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="structuralHash">A span-insensitive hash of this node.</param>
        private protected SyntaxLeaf(SyntaxKind kind, QueryexSpan span, int structuralHash)
            : base(kind, span, depth: 1, nodeCount: 1, structuralHash)
        {
        }

        /// <inheritdoc />
        internal override IReadOnlyList<SyntaxNode> Children => [];
    }

    /// <summary>A numeric literal.</summary>
    internal sealed class NumberSyntax : SyntaxLeaf
    {
        /// <summary>Initializes the literal.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="value">The denoted value.</param>
        /// <param name="precision">The significant-digit count.</param>
        /// <param name="scale">The written scale.</param>
        /// <param name="text">The digits as written, normalized for printing.</param>
        internal NumberSyntax(QueryexSpan span, SqlDecimal value, byte precision, byte scale, string text)
            : base(SyntaxKind.Number, span, HashCode.Combine(SyntaxKind.Number, text))
        {
            Value = value;
            Precision = precision;
            Scale = scale;
            Text = text;
        }

        /// <summary>The denoted value.</summary>
        internal SqlDecimal Value { get; }

        /// <summary>The significant-digit count.</summary>
        internal byte Precision { get; }

        /// <summary>The written scale, which is load-bearing and therefore never normalized away.</summary>
        internal byte Scale { get; }

        /// <summary>The digits as written.</summary>
        internal string Text { get; }
    }

    /// <summary>A string literal.</summary>
    internal sealed class StringSyntax : SyntaxLeaf
    {
        /// <summary>Initializes the literal.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="value">The denoted text, with doubled quotes already collapsed.</param>
        internal StringSyntax(QueryexSpan span, string value)
            : base(SyntaxKind.String, span, HashCode.Combine(SyntaxKind.String, value))
        {
            Value = value;
        }

        /// <summary>The denoted text.</summary>
        internal string Value { get; }
    }

    /// <summary>A boolean literal.</summary>
    internal sealed class BooleanSyntax : SyntaxLeaf
    {
        /// <summary>Initializes the literal.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="value">The denoted value.</param>
        internal BooleanSyntax(QueryexSpan span, bool value)
            : base(SyntaxKind.Boolean, span, HashCode.Combine(SyntaxKind.Boolean, value))
        {
            Value = value;
        }

        /// <summary>The denoted value.</summary>
        internal bool Value { get; }
    }

    /// <summary>The absent-value literal.</summary>
    internal sealed class NullSyntax : SyntaxLeaf
    {
        /// <summary>Initializes the literal.</summary>
        /// <param name="span">The node's range in the input.</param>
        internal NullSyntax(QueryexSpan span)
            : base(SyntaxKind.Null, span, HashCode.Combine(SyntaxKind.Null))
        {
        }
    }

    /// <summary>A named parameter.</summary>
    internal sealed class ParameterSyntax : SyntaxLeaf
    {
        /// <summary>Initializes the parameter.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="name">The parameter name, without its marker.</param>
        internal ParameterSyntax(QueryexSpan span, string name)
            : base(
                SyntaxKind.Parameter,
                span,
                HashCode.Combine(SyntaxKind.Parameter, name.ToUpperInvariant()))
        {
            Name = name;
        }

        /// <summary>The parameter name, without its marker. Matched case-insensitively.</summary>
        internal string Name { get; }
    }

    /// <summary>One segment of a path.</summary>
    /// <param name="Name">The segment name, without brackets.</param>
    /// <param name="Span">The segment's own range, so a diagnostic can point at it alone.</param>
    /// <param name="WasBracketed">Whether it was written in brackets.</param>
    internal readonly record struct PathSegment(string Name, QueryexSpan Span, bool WasBracketed);

    /// <summary>A dotted path.</summary>
    internal sealed class PathSyntax : SyntaxLeaf
    {
        /// <summary>Initializes the path.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="segments">The segments, in source order.</param>
        internal PathSyntax(QueryexSpan span, IReadOnlyList<PathSegment> segments)
            : base(SyntaxKind.Path, span, HashSegments(segments))
        {
            Segments = segments;
        }

        /// <summary>The segments, in source order.</summary>
        internal IReadOnlyList<PathSegment> Segments { get; }

        /// <summary>Folds the segment names into a hash, case-insensitively.</summary>
        /// <param name="segments">The segments.</param>
        /// <returns>The hash.</returns>
        private static int HashSegments(IReadOnlyList<PathSegment> segments)
        {
            HashCode hash = default;
            hash.Add((int)SyntaxKind.Path);
            for (int index = 0; index < segments.Count; index++)
            {
                hash.Add(segments[index].Name, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>A function call.</summary>
    internal sealed class CallSyntax : SyntaxNode
    {
        /// <summary>Initializes the call.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="name">The function name as written.</param>
        /// <param name="nameSpan">The function name's own range.</param>
        /// <param name="arguments">The arguments, in source order.</param>
        internal CallSyntax(
            QueryexSpan span,
            string name,
            QueryexSpan nameSpan,
            IReadOnlyList<SyntaxNode> arguments)
            : base(
                SyntaxKind.Call,
                span,
                DepthOf(arguments),
                CountOf(arguments),
                HashOf(SyntaxKind.Call, StringComparer.OrdinalIgnoreCase.GetHashCode(name), arguments))
        {
            Name = name;
            NameSpan = nameSpan;
            Arguments = arguments;
        }

        /// <summary>The function name as written. Matched case-insensitively.</summary>
        internal string Name { get; }

        /// <summary>The function name's own range, so an unknown-function diagnostic points at it.</summary>
        internal QueryexSpan NameSpan { get; }

        /// <summary>The arguments, in source order.</summary>
        internal IReadOnlyList<SyntaxNode> Arguments { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<SyntaxNode> Children => Arguments;
    }

    /// <summary>A prefix operator application.</summary>
    internal sealed class UnarySyntax : SyntaxNode
    {
        /// <summary>The single operand, as a list.</summary>
        private readonly SyntaxNode[] _children;

        /// <summary>Initializes the application.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="operatorKind">The operator.</param>
        /// <param name="operatorSpan">The operator's own range.</param>
        /// <param name="operand">The operand.</param>
        internal UnarySyntax(
            QueryexSpan span,
            UnaryOperatorKind operatorKind,
            QueryexSpan operatorSpan,
            SyntaxNode operand)
            : base(
                SyntaxKind.Unary,
                span,
                operand.Depth + 1,
                operand.NodeCount + 1,
                HashCode.Combine(SyntaxKind.Unary, operatorKind, operand.StructuralHash))
        {
            OperatorKind = operatorKind;
            OperatorSpan = operatorSpan;
            Operand = operand;
            _children = [operand];
        }

        /// <summary>The operator.</summary>
        internal UnaryOperatorKind OperatorKind { get; }

        /// <summary>The operator's own range.</summary>
        internal QueryexSpan OperatorSpan { get; }

        /// <summary>The operand.</summary>
        internal SyntaxNode Operand { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<SyntaxNode> Children => _children;
    }

    /// <summary>An infix operator application.</summary>
    internal sealed class BinarySyntax : SyntaxNode
    {
        /// <summary>The two operands, as a list.</summary>
        private readonly SyntaxNode[] _children;

        /// <summary>Initializes the application.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="operatorKind">The operator.</param>
        /// <param name="operatorSpan">The operator's own range.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        internal BinarySyntax(
            QueryexSpan span,
            BinaryOperatorKind operatorKind,
            QueryexSpan operatorSpan,
            SyntaxNode left,
            SyntaxNode right)
            : base(
                SyntaxKind.Binary,
                span,
                Math.Max(left.Depth, right.Depth) + 1,
                left.NodeCount + right.NodeCount + 1,
                HashCode.Combine(SyntaxKind.Binary, operatorKind, left.StructuralHash, right.StructuralHash))
        {
            OperatorKind = operatorKind;
            OperatorSpan = operatorSpan;
            Left = left;
            Right = right;
            _children = [left, right];
        }

        /// <summary>The operator.</summary>
        internal BinaryOperatorKind OperatorKind { get; }

        /// <summary>The operator's own range, so a type diagnostic can point at the operator.</summary>
        internal QueryexSpan OperatorSpan { get; }

        /// <summary>The left operand.</summary>
        internal SyntaxNode Left { get; }

        /// <summary>The right operand.</summary>
        internal SyntaxNode Right { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<SyntaxNode> Children => _children;
    }

    /// <summary>A set-membership test.</summary>
    internal sealed class InSyntax : SyntaxNode
    {
        /// <summary>The value followed by the elements.</summary>
        private readonly SyntaxNode[] _children;

        /// <summary>Initializes the test.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="keywordSpan">The keyword's own range.</param>
        /// <param name="value">The value being tested.</param>
        /// <param name="elements">The elements, in source order.</param>
        internal InSyntax(
            QueryexSpan span,
            QueryexSpan keywordSpan,
            SyntaxNode value,
            IReadOnlyList<SyntaxNode> elements)
            : base(
                SyntaxKind.In,
                span,
                DepthOf([value, .. elements]),
                CountOf([value, .. elements]),
                HashOf(SyntaxKind.In, 0, [value, .. elements]))
        {
            KeywordSpan = keywordSpan;
            Value = value;
            Elements = elements;
            _children = [value, .. elements];
        }

        /// <summary>The keyword's own range.</summary>
        internal QueryexSpan KeywordSpan { get; }

        /// <summary>The value being tested.</summary>
        internal SyntaxNode Value { get; }

        /// <summary>The elements, in source order.</summary>
        internal IReadOnlyList<SyntaxNode> Elements { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<SyntaxNode> Children => _children;
    }

    /// <summary>An absence test.</summary>
    internal sealed class IsNullSyntax : SyntaxNode
    {
        /// <summary>The single operand, as a list.</summary>
        private readonly SyntaxNode[] _children;

        /// <summary>Initializes the test.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="keywordSpan">The keyword's own range.</param>
        /// <param name="operand">The operand.</param>
        /// <param name="negated">Whether the test was written negated.</param>
        internal IsNullSyntax(
            QueryexSpan span,
            QueryexSpan keywordSpan,
            SyntaxNode operand,
            bool negated)
            : base(
                SyntaxKind.IsNull,
                span,
                operand.Depth + 1,
                operand.NodeCount + 1,
                HashCode.Combine(SyntaxKind.IsNull, negated, operand.StructuralHash))
        {
            KeywordSpan = keywordSpan;
            Operand = operand;
            Negated = negated;
            _children = [operand];
        }

        /// <summary>The keyword's own range.</summary>
        internal QueryexSpan KeywordSpan { get; }

        /// <summary>The operand.</summary>
        internal SyntaxNode Operand { get; }

        /// <summary>Whether the test was written negated.</summary>
        internal bool Negated { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<SyntaxNode> Children => _children;
    }

    /// <summary>
    ///     A parenthesised expression.
    /// </summary>
    /// <remarks>
    ///     Kept as a real node rather than discarded, because spans have to stay exact and because
    ///     the rules that demand a bare path as an argument need a precise answer for a path someone
    ///     wrapped in parentheses. Every consumer that does not care sees through it.
    /// </remarks>
    internal sealed class ParenthesizedSyntax : SyntaxNode
    {
        /// <summary>The single child, as a list.</summary>
        private readonly SyntaxNode[] _children;

        /// <summary>Initializes the node.</summary>
        /// <param name="span">The node's range, brackets included.</param>
        /// <param name="inner">The enclosed expression.</param>
        internal ParenthesizedSyntax(QueryexSpan span, SyntaxNode inner)
            : base(
                SyntaxKind.Parenthesized,
                span,
                inner.Depth + 1,
                inner.NodeCount + 1,

                // The inner hash verbatim: parentheses change grouping, which the tree already
                // records, and never change what the expression is.
                inner.StructuralHash)
        {
            Inner = inner;
            _children = [inner];
        }

        /// <summary>The enclosed expression.</summary>
        internal SyntaxNode Inner { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<SyntaxNode> Children => _children;
    }

    /// <summary>
    ///     A region the parser could not read.
    /// </summary>
    /// <remarks>
    ///     Produced only by recovery. It binds to an error, and every judgement involving an error
    ///     succeeds silently, so a recovered region suppresses the cascade of complaints that would
    ///     otherwise follow one real mistake.
    /// </remarks>
    internal sealed class ErrorSyntax : SyntaxLeaf
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">The unreadable range.</param>
        /// <param name="sourceSlice">The text of that range, so a printer can round-trip it.</param>
        internal ErrorSyntax(QueryexSpan span, string sourceSlice)
            : base(SyntaxKind.Error, span, HashCode.Combine(SyntaxKind.Error, sourceSlice))
        {
            SourceSlice = sourceSlice;
        }

        /// <summary>The text of the unreadable range.</summary>
        internal string SourceSlice { get; }
    }

    /// <summary>One item of a list, with its optional direction suffix.</summary>
    internal sealed class ExpressionItemSyntax : SyntaxNode
    {
        /// <summary>The single child, as a list.</summary>
        private readonly SyntaxNode[] _children;

        /// <summary>Initializes the item.</summary>
        /// <param name="span">The item's range, direction suffix included.</param>
        /// <param name="expression">The item's expression.</param>
        /// <param name="direction">The direction suffix, when one was written.</param>
        /// <param name="directionSpan">The direction suffix's own range.</param>
        internal ExpressionItemSyntax(
            QueryexSpan span,
            SyntaxNode expression,
            QueryexDirection direction,
            QueryexSpan directionSpan)
            : base(
                SyntaxKind.ExpressionItem,
                span,
                expression.Depth,
                expression.NodeCount + 1,
                HashCode.Combine(SyntaxKind.ExpressionItem, direction, expression.StructuralHash))
        {
            Expression = expression;
            Direction = direction;
            DirectionSpan = directionSpan;
            _children = [expression];
        }

        /// <summary>The item's expression.</summary>
        internal SyntaxNode Expression { get; }

        /// <summary>The direction suffix, when one was written.</summary>
        internal QueryexDirection Direction { get; }

        /// <summary>The direction suffix's own range.</summary>
        internal QueryexSpan DirectionSpan { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<SyntaxNode> Children => _children;
    }

    /// <summary>A comma-separated list of items: what a parse of one input text yields.</summary>
    internal sealed class ExpressionListSyntax : SyntaxNode
    {
        /// <summary>Initializes the list.</summary>
        /// <param name="span">The whole input's range.</param>
        /// <param name="items">The items, in source order.</param>
        /// <param name="referencedParameters">
        ///     Every parameter name the list mentions, deduplicated and sorted.
        /// </param>
        internal ExpressionListSyntax(
            QueryexSpan span,
            IReadOnlyList<ExpressionItemSyntax> items,
            IReadOnlyList<string> referencedParameters)
            : base(
                SyntaxKind.ExpressionList,
                span,
                DepthOf(items),
                CountOf(items),
                HashOf(SyntaxKind.ExpressionList, 0, items))
        {
            Items = items;
            ReferencedParameters = referencedParameters;
        }

        /// <summary>The items, in source order.</summary>
        internal IReadOnlyList<ExpressionItemSyntax> Items { get; }

        /// <summary>
        ///     Every parameter name the list mentions, deduplicated and sorted.
        /// </summary>
        /// <remarks>
        ///     Collected during parsing because parameters are lexically identifiable — no schema and
        ///     no binding needed. That is what lets the cache key for a bound expression carry only
        ///     the declarations this text could actually be affected by, instead of every declaration
        ///     the caller happened to supply.
        /// </remarks>
        internal IReadOnlyList<string> ReferencedParameters { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<SyntaxNode> Children => Items;
    }
}
