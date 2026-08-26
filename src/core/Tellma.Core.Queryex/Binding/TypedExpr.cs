// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Emit;
using Tellma.Core.Queryex.Functions;
using Tellma.Core.Queryex.Nullity;

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>The kind of one bound node.</summary>
    internal enum TypedExprKind
    {
        /// <summary>A literal value.</summary>
        Literal,

        /// <summary>The absent-value literal.</summary>
        Null,

        /// <summary>A resolved path.</summary>
        Path,

        /// <summary>A declared parameter.</summary>
        Parameter,

        /// <summary>Arithmetic negation.</summary>
        Negate,

        /// <summary>Arithmetic or concatenation.</summary>
        Arithmetic,

        /// <summary>A comparison.</summary>
        Comparison,

        /// <summary>A set-membership test.</summary>
        In,

        /// <summary>An absence test.</summary>
        IsNull,

        /// <summary>A conjunction or a disjunction.</summary>
        Logical,

        /// <summary>Logical negation.</summary>
        Not,

        /// <summary>A resolved function call.</summary>
        Call,

        /// <summary>A node that failed to bind.</summary>
        Error,
    }

    /// <summary>Facts folded up from a node's children when it is built.</summary>
    [Flags]
    internal enum TypedExprFlags
    {
        /// <summary>Nothing of note.</summary>
        None = 0,

        /// <summary>This subtree contains an aggregation.</summary>
        ContainsAggregate = 1 << 0,

        /// <summary>This subtree reads at least one path.</summary>
        ContainsPath = 1 << 1,

        /// <summary>This subtree reads at least one declared parameter.</summary>
        ContainsParameter = 1 << 2,

        /// <summary>
        ///     This node costs nothing to emit twice: a column, a parameter slot, or a value the
        ///     host supplies once per statement.
        /// </summary>
        Atomic = 1 << 3,

        /// <summary>This subtree contains a node that failed to bind.</summary>
        ContainsError = 1 << 4,
    }

    /// <summary>One resolved logical connective.</summary>
    internal enum LogicalOperator
    {
        /// <summary>Conjunction.</summary>
        And,

        /// <summary>Disjunction.</summary>
        Or,
    }

    /// <summary>One resolved arithmetic or concatenation operator.</summary>
    internal enum ArithmeticOperator
    {
        /// <summary>Addition.</summary>
        Add,

        /// <summary>Subtraction.</summary>
        Subtract,

        /// <summary>Multiplication.</summary>
        Multiply,

        /// <summary>Division.</summary>
        Divide,

        /// <summary>Remainder.</summary>
        Remainder,

        /// <summary>Concatenation.</summary>
        Concat,
    }

    /// <summary>One resolved comparison operator.</summary>
    internal enum ComparisonOperator
    {
        /// <summary>Equality.</summary>
        Equal,

        /// <summary>Inequality.</summary>
        NotEqual,

        /// <summary>Strictly less.</summary>
        Less,

        /// <summary>Less or equal.</summary>
        LessOrEqual,

        /// <summary>Strictly greater.</summary>
        Greater,

        /// <summary>Greater or equal.</summary>
        GreaterOrEqual,
    }

    /// <summary>A declared parameter, as the binder sees it.</summary>
    /// <param name="Name">The declared name, in the casing the declaration used.</param>
    /// <param name="Type">The declared type.</param>
    /// <param name="Nullity">Whether a value is guaranteed present.</param>
    internal sealed record ParameterSymbol(string Name, BoundType Type, QueryexNullity Nullity);

    /// <summary>An argument the engine supplies itself, with its value already resolved.</summary>
    /// <param name="Origin">Where the value comes from.</param>
    /// <param name="Type">Its type.</param>
    /// <param name="Value">The value, when it was fixed at compile time.</param>
    internal sealed record ResolvedSynthetic(QueryexParameterOrigin Origin, BoundType Type, object? Value);

    /// <summary>
    ///     One node of a bound tree: typed, resolved to descriptors, and free of any SQL.
    /// </summary>
    /// <remarks>
    ///     Holds descriptors rather than the names an author wrote, which is what makes it
    ///     impossible for a cached tree to emit a column that has since been renamed: the schema
    ///     version moves, and the cache entry dies with it. The text survives only as spans, for
    ///     diagnostics.
    /// </remarks>
    internal abstract class TypedExpr
    {
        /// <summary>Initializes the node with the facts folded up from its children.</summary>
        /// <param name="kind">The node kind.</param>
        /// <param name="type">The node's type.</param>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="flags">Facts folded up from the children.</param>
        /// <param name="nodeCount">The nodes in this subtree, this one included.</param>
        /// <param name="structuralHash">A span-insensitive hash of this subtree.</param>
        private protected TypedExpr(
            TypedExprKind kind,
            BoundType type,
            QueryexSpan span,
            TypedExprFlags flags,
            int nodeCount,
            int structuralHash)
        {
            Kind = kind;
            Type = type;
            Span = span;
            Flags = flags;
            NodeCount = nodeCount;
            StructuralHash = structuralHash;
        }

        /// <summary>The node kind.</summary>
        internal TypedExprKind Kind { get; }

        /// <summary>The node's type.</summary>
        internal BoundType Type { get; }

        /// <summary>The node's range in the input.</summary>
        internal QueryexSpan Span { get; }

        /// <summary>Facts folded up from the children.</summary>
        internal TypedExprFlags Flags { get; }

        /// <summary>The nodes in this subtree, this one included.</summary>
        internal int NodeCount { get; }

        /// <summary>
        ///     A hash of this subtree that ignores spans.
        /// </summary>
        /// <remarks>
        ///     For bucketing and for rejecting inequality only. The underlying hash is seeded per
        ///     process, so anything that sorted by it would produce different output on different
        ///     runs — and byte-identical output is the property plan caching and golden files rest
        ///     on.
        /// </remarks>
        internal int StructuralHash { get; }

        /// <summary>This node's children, in source order.</summary>
        internal abstract IReadOnlyList<TypedExpr> Children { get; }

        /// <summary>Whether this subtree contains an aggregation.</summary>
        internal bool ContainsAggregate => (Flags & TypedExprFlags.ContainsAggregate) != 0;

        /// <summary>Whether this subtree reads at least one path.</summary>
        internal bool ContainsPath => (Flags & TypedExprFlags.ContainsPath) != 0;

        /// <summary>Whether this subtree contains a node that failed to bind.</summary>
        internal bool ContainsError => (Flags & TypedExprFlags.ContainsError) != 0;

        /// <summary>Whether this node costs nothing to emit twice.</summary>
        internal bool IsAtomic => (Flags & TypedExprFlags.Atomic) != 0;

        /// <summary>Folds the children's facts, keeping only those that propagate upward.</summary>
        /// <param name="children">The children.</param>
        /// <returns>The folded facts.</returns>
        private protected static TypedExprFlags Fold(IReadOnlyList<TypedExpr> children)
        {
            TypedExprFlags flags = TypedExprFlags.None;
            for (int index = 0; index < children.Count; index++)
            {
                // Atomicity is a property of the node itself rather than of its subtree, so it is
                // the one fact that does not travel upward.
                flags |= children[index].Flags & ~TypedExprFlags.Atomic;
            }

            return flags;
        }

        /// <summary>The total node count of the given subtrees, plus one.</summary>
        /// <param name="children">The children.</param>
        /// <returns>This node's subtree size.</returns>
        private protected static int CountOf(IReadOnlyList<TypedExpr> children)
        {
            int total = 1;
            for (int index = 0; index < children.Count; index++)
            {
                total += children[index].NodeCount;
            }

            return total;
        }

        /// <summary>Folds the children's structural hashes into one.</summary>
        /// <param name="kind">The node kind.</param>
        /// <param name="discriminator">A per-kind discriminator, such as an operator.</param>
        /// <param name="children">The children.</param>
        /// <returns>This node's structural hash.</returns>
        private protected static int HashOf(
            TypedExprKind kind,
            int discriminator,
            IReadOnlyList<TypedExpr> children)
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

    /// <summary>A bound node with no children.</summary>
    internal abstract class TypedLeaf : TypedExpr
    {
        /// <summary>Initializes the leaf.</summary>
        /// <param name="kind">The node kind.</param>
        /// <param name="type">The node's type.</param>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="flags">The node's own facts.</param>
        /// <param name="structuralHash">A span-insensitive hash of this node.</param>
        private protected TypedLeaf(
            TypedExprKind kind,
            BoundType type,
            QueryexSpan span,
            TypedExprFlags flags,
            int structuralHash)
            : base(kind, type, span, flags, nodeCount: 1, structuralHash)
        {
        }

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => [];
    }

    /// <summary>A literal value.</summary>
    internal sealed class TypedLiteral : TypedLeaf
    {
        /// <summary>Initializes the literal.</summary>
        /// <param name="type">The literal's type.</param>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="value">The denoted value.</param>
        /// <param name="precision">The significant-digit count, for a number.</param>
        /// <param name="scale">The written scale, for a number.</param>
        internal TypedLiteral(
            BoundType type,
            QueryexSpan span,
            object value,
            byte precision = 0,
            byte scale = 0)
            : base(
                TypedExprKind.Literal,
                type,
                span,
                TypedExprFlags.Atomic,
                HashCode.Combine(TypedExprKind.Literal, type, value, scale))
        {
            Value = value;
            Precision = precision;
            Scale = scale;
        }

        /// <summary>The denoted value.</summary>
        internal object Value { get; }

        /// <summary>The significant-digit count, for a number.</summary>
        internal byte Precision { get; }

        /// <summary>
        ///     The written scale, for a number. Part of the node's identity: two numbers that are
        ///     equal as values but written with different scales bind their values differently, so
        ///     treating them as one would type a parameter slot on the wrong one.
        /// </summary>
        internal byte Scale { get; }
    }

    /// <summary>The absent-value literal.</summary>
    internal sealed class TypedNull : TypedLeaf
    {
        /// <summary>Initializes the literal.</summary>
        /// <param name="type">The type its context demanded, or the internal absent type.</param>
        /// <param name="span">The node's range in the input.</param>
        internal TypedNull(BoundType type, QueryexSpan span)
            : base(
                TypedExprKind.Null,
                type,
                span,
                TypedExprFlags.Atomic,
                HashCode.Combine(TypedExprKind.Null, type))
        {
        }
    }

    /// <summary>A resolved path.</summary>
    internal sealed class TypedPath : TypedLeaf
    {
        /// <summary>Initializes the path.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="navigations">The navigation steps, in order.</param>
        /// <param name="property">The scalar property the path ends at.</param>
        /// <param name="segments">The logical segment names, for reporting a column's path.</param>
        internal TypedPath(
            QueryexSpan span,
            ImmutableArray<NavigationDescriptor> navigations,
            PropertyDescriptor property,
            ImmutableArray<string> segments)
            : base(
                TypedExprKind.Path,
                BoundTypes.FromPublic(property.Type),
                span,
                TypedExprFlags.Atomic | TypedExprFlags.ContainsPath,
                HashPath(navigations, property))
        {
            Navigations = navigations;
            Property = property;
            Segments = segments;
        }

        /// <summary>The navigation steps, in order.</summary>
        internal ImmutableArray<NavigationDescriptor> Navigations { get; }

        /// <summary>The scalar property the path ends at.</summary>
        internal PropertyDescriptor Property { get; }

        /// <summary>The logical segment names, as the author wrote them.</summary>
        internal ImmutableArray<string> Segments { get; }

        /// <summary>Hashes a path by the descriptors it resolved to.</summary>
        /// <param name="navigations">The navigation steps.</param>
        /// <param name="property">The final property.</param>
        /// <returns>The hash.</returns>
        private static int HashPath(
            ImmutableArray<NavigationDescriptor> navigations,
            PropertyDescriptor property)
        {
            HashCode hash = default;
            hash.Add((int)TypedExprKind.Path);
            foreach (NavigationDescriptor navigation in navigations)
            {
                hash.Add(navigation.Name, StringComparer.OrdinalIgnoreCase);
            }

            hash.Add(property.Name, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }

    /// <summary>A declared parameter.</summary>
    internal sealed class TypedParameter : TypedLeaf
    {
        /// <summary>Initializes the parameter.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="symbol">The declaration it resolved to.</param>
        internal TypedParameter(QueryexSpan span, ParameterSymbol symbol)
            : base(
                TypedExprKind.Parameter,
                symbol.Type,
                span,
                TypedExprFlags.Atomic | TypedExprFlags.ContainsParameter,
                HashCode.Combine(TypedExprKind.Parameter, symbol.Name.ToUpperInvariant(), symbol.Type))
        {
            Symbol = symbol;
        }

        /// <summary>The declaration it resolved to.</summary>
        internal ParameterSymbol Symbol { get; }
    }

    /// <summary>A node with exactly one child.</summary>
    internal abstract class TypedUnaryBase : TypedExpr
    {
        /// <summary>The single operand, as a list.</summary>
        private readonly TypedExpr[] _children;

        /// <summary>Initializes the node.</summary>
        /// <param name="kind">The node kind.</param>
        /// <param name="type">The node's type.</param>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="operand">The operand.</param>
        /// <param name="discriminator">A per-kind discriminator.</param>
        /// <param name="extraFlags">Facts this node adds of its own.</param>
        private protected TypedUnaryBase(
            TypedExprKind kind,
            BoundType type,
            QueryexSpan span,
            TypedExpr operand,
            int discriminator,
            TypedExprFlags extraFlags = TypedExprFlags.None)
            : base(
                kind,
                type,
                span,
                Fold([operand]) | extraFlags,
                operand.NodeCount + 1,
                HashCode.Combine(kind, discriminator, operand.StructuralHash))
        {
            Operand = operand;
            _children = [operand];
        }

        /// <summary>The operand.</summary>
        internal TypedExpr Operand { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => _children;
    }

    /// <summary>Arithmetic negation.</summary>
    internal sealed class TypedNegate : TypedUnaryBase
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="operand">The operand.</param>
        internal TypedNegate(QueryexSpan span, TypedExpr operand)
            : base(TypedExprKind.Negate, operand.Type, span, operand, 0)
        {
        }
    }

    /// <summary>Logical negation.</summary>
    internal sealed class TypedNot : TypedUnaryBase
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="operand">The operand.</param>
        internal TypedNot(QueryexSpan span, TypedExpr operand)
            : base(TypedExprKind.Not, BoundType.Bool, span, operand, 0)
        {
        }
    }

    /// <summary>An absence test.</summary>
    internal sealed class TypedIsNull : TypedUnaryBase
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="operand">The operand.</param>
        /// <param name="negated">Whether the test was written negated.</param>
        internal TypedIsNull(QueryexSpan span, TypedExpr operand, bool negated)
            : base(TypedExprKind.IsNull, BoundType.Bool, span, operand, negated ? 1 : 0)
        {
            Negated = negated;
        }

        /// <summary>Whether the test was written negated.</summary>
        internal bool Negated { get; }
    }

    /// <summary>Arithmetic or concatenation.</summary>
    internal sealed class TypedArithmetic : TypedExpr
    {
        /// <summary>The two operands, as a list.</summary>
        private readonly TypedExpr[] _children;

        /// <summary>Initializes the node.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="op">The operator.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        internal TypedArithmetic(
            QueryexSpan span,
            ArithmeticOperator op,
            TypedExpr left,
            TypedExpr right)
            : base(
                TypedExprKind.Arithmetic,
                op == ArithmeticOperator.Concat ? BoundType.String : BoundType.Numeric,
                span,
                Fold([left, right]),
                left.NodeCount + right.NodeCount + 1,
                HashCode.Combine(TypedExprKind.Arithmetic, op, left.StructuralHash, right.StructuralHash))
        {
            Operator = op;
            Left = left;
            Right = right;
            _children = [left, right];
        }

        /// <summary>The operator.</summary>
        internal ArithmeticOperator Operator { get; }

        /// <summary>The left operand.</summary>
        internal TypedExpr Left { get; }

        /// <summary>The right operand.</summary>
        internal TypedExpr Right { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => _children;
    }

    /// <summary>A comparison.</summary>
    internal sealed class TypedComparison : TypedExpr
    {
        /// <summary>The two operands, as a list.</summary>
        private readonly TypedExpr[] _children;

        /// <summary>Initializes the node.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="op">The operator.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        internal TypedComparison(
            QueryexSpan span,
            ComparisonOperator op,
            TypedExpr left,
            TypedExpr right)
            : base(
                TypedExprKind.Comparison,
                BoundType.Bool,
                span,
                Fold([left, right]),
                left.NodeCount + right.NodeCount + 1,
                HashCode.Combine(TypedExprKind.Comparison, op, left.StructuralHash, right.StructuralHash))
        {
            Operator = op;
            Left = left;
            Right = right;
            _children = [left, right];
        }

        /// <summary>The operator.</summary>
        internal ComparisonOperator Operator { get; }

        /// <summary>The left operand.</summary>
        internal TypedExpr Left { get; }

        /// <summary>The right operand.</summary>
        internal TypedExpr Right { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => _children;
    }

    /// <summary>A set-membership test.</summary>
    internal sealed class TypedIn : TypedExpr
    {
        /// <summary>The value followed by the elements.</summary>
        private readonly TypedExpr[] _children;

        /// <summary>Initializes the node.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="value">The value being tested.</param>
        /// <param name="elements">The elements.</param>
        internal TypedIn(QueryexSpan span, TypedExpr value, ImmutableArray<TypedExpr> elements)
            : base(
                TypedExprKind.In,
                BoundType.Bool,
                span,
                Fold([value, .. elements]),
                CountOf([value, .. elements]),
                HashOf(TypedExprKind.In, 0, [value, .. elements]))
        {
            Value = value;
            Elements = elements;
            _children = [value, .. elements];
        }

        /// <summary>The value being tested.</summary>
        internal TypedExpr Value { get; }

        /// <summary>The elements.</summary>
        internal ImmutableArray<TypedExpr> Elements { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => _children;
    }

    /// <summary>
    ///     A conjunction or a disjunction, flattened.
    /// </summary>
    /// <remarks>
    ///     Held as one node over many operands rather than as a chain of pairs, so that differently
    ///     nested writings of the same conjunction are one thing. That is what makes structural
    ///     matching mean what a reader expects, and what lets a composed filter tree become a single
    ///     node.
    /// </remarks>
    internal sealed class TypedLogical : TypedExpr
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="op">The connective.</param>
        /// <param name="operands">The operands, in order.</param>
        internal TypedLogical(QueryexSpan span, LogicalOperator op, ImmutableArray<TypedExpr> operands)
            : base(
                TypedExprKind.Logical,
                BoundType.Bool,
                span,
                Fold(operands),
                CountOf(operands),
                HashOf(TypedExprKind.Logical, (int)op, operands))
        {
            Operator = op;
            Operands = operands;
        }

        /// <summary>The connective.</summary>
        internal LogicalOperator Operator { get; }

        /// <summary>The operands, in order.</summary>
        internal ImmutableArray<TypedExpr> Operands { get; }

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => Operands;
    }

    /// <summary>A resolved function call.</summary>
    internal sealed class TypedCall : TypedExpr
    {
        /// <summary>Initializes the call.</summary>
        /// <param name="span">The node's range in the input.</param>
        /// <param name="type">The result type.</param>
        /// <param name="definition">The function.</param>
        /// <param name="signature">The overload that won.</param>
        /// <param name="arguments">The arguments, with any variadic tail already expanded.</param>
        /// <param name="restStart">Where the variadic tail begins, or -1 when there is none.</param>
        /// <param name="selectors">The resolved value of each consumed selector, by parameter.</param>
        /// <param name="synthetic">The arguments the engine supplies itself.</param>
        internal TypedCall(
            QueryexSpan span,
            BoundType type,
            FunctionDefinition definition,
            FunctionSignature signature,
            ImmutableArray<TypedExpr> arguments,
            int restStart,
            ImmutableArray<string?> selectors,
            ImmutableArray<ResolvedSynthetic> synthetic)
            : base(
                TypedExprKind.Call,
                type,
                span,
                Fold(arguments)
                    | (definition.Category == FunctionCategory.Aggregate
                        ? TypedExprFlags.ContainsAggregate
                        : TypedExprFlags.None)
                    | (signature.Emit is ContextStrategy ? TypedExprFlags.Atomic : TypedExprFlags.None),
                CountOf(arguments),
                HashCall(definition, signature, selectors, arguments))
        {
            Definition = definition;
            Signature = signature;
            Arguments = arguments;
            RestStart = restStart;
            Selectors = selectors;
            Synthetic = synthetic;
        }

        /// <summary>The function.</summary>
        internal FunctionDefinition Definition { get; }

        /// <summary>The overload that won.</summary>
        internal FunctionSignature Signature { get; }

        /// <summary>The arguments, with any variadic tail already expanded.</summary>
        internal ImmutableArray<TypedExpr> Arguments { get; }

        /// <summary>Where the variadic tail begins, or -1 when there is none.</summary>
        internal int RestStart { get; }

        /// <summary>The resolved value of each consumed selector, by parameter position.</summary>
        internal ImmutableArray<string?> Selectors { get; }

        /// <summary>The arguments the engine supplies itself, appended after the declared ones.</summary>
        internal ImmutableArray<ResolvedSynthetic> Synthetic { get; }

        /// <summary>Whether this call is an aggregation.</summary>
        internal bool IsAggregate => Definition.Category == FunctionCategory.Aggregate;

        /// <inheritdoc />
        internal override IReadOnlyList<TypedExpr> Children => Arguments;

        /// <summary>Hashes a call by the overload it resolved to and what it was given.</summary>
        /// <param name="definition">The function.</param>
        /// <param name="signature">The overload.</param>
        /// <param name="selectors">The resolved selectors.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The hash.</returns>
        private static int HashCall(
            FunctionDefinition definition,
            FunctionSignature signature,
            ImmutableArray<string?> selectors,
            ImmutableArray<TypedExpr> arguments)
        {
            HashCode hash = default;
            hash.Add((int)TypedExprKind.Call);
            hash.Add(definition.Name, StringComparer.OrdinalIgnoreCase);
            hash.Add(definition.Signatures.IndexOf(signature));
            foreach (string? selector in selectors)
            {
                hash.Add(selector, StringComparer.OrdinalIgnoreCase);
            }

            foreach (TypedExpr argument in arguments)
            {
                hash.Add(argument.StructuralHash);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>
    ///     A node that failed to bind.
    /// </summary>
    /// <remarks>
    ///     Never equal to anything, itself included: an error must not accidentally match a grouping
    ///     key or an ordering term, and two independent failures are not the same failure.
    /// </remarks>
    internal sealed class TypedError : TypedLeaf
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">The node's range in the input.</param>
        internal TypedError(QueryexSpan span)
            : base(
                TypedExprKind.Error,
                BoundType.Error,
                span,
                TypedExprFlags.ContainsError,
                HashCode.Combine(TypedExprKind.Error))
        {
        }
    }

    /// <summary>One bound item of an expression list.</summary>
    /// <param name="Expression">The item's bound expression.</param>
    /// <param name="Span">The item's range in the input.</param>
    /// <param name="Direction">The direction suffix, when one was written.</param>
    internal sealed record BoundItem(TypedExpr Expression, QueryexSpan Span, QueryexDirection Direction);

    /// <summary>
    ///     One bound expression list, with everything derived from it that later stages need.
    /// </summary>
    /// <param name="Items">The items, in source order.</param>
    /// <param name="Nullity">Each node's nullity, kept beside the tree rather than on it.</param>
    /// <param name="NodeCount">The bound nodes across every item.</param>
    /// <param name="Diagnostics">Whatever problems binding reported, without a location.</param>
    internal sealed record BoundExpression(
        ImmutableArray<BoundItem> Items,
        NullityMap Nullity,
        int NodeCount,
        ImmutableArray<Diagnostics.CachedDiagnostic> Diagnostics);
}
