// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Emit;

namespace Tellma.Core.Queryex.Functions
{
    /// <summary>Whether a function reads one row or a whole group.</summary>
    internal enum FunctionCategory
    {
        /// <summary>Reads the current row.</summary>
        Scalar,

        /// <summary>Reads the rows of a group.</summary>
        Aggregate,
    }

    /// <summary>What kind of restriction an argument carries beyond its type.</summary>
    internal enum ParameterConstraintKind
    {
        /// <summary>None.</summary>
        None,

        /// <summary>The argument has to be a literal.</summary>
        LiteralOnly,

        /// <summary>The argument has to be a literal drawn from a closed set.</summary>
        MemberOf,

        /// <summary>
        ///     The argument has to be a bare path whose entity is hierarchical and whose property is
        ///     unique, so that each key identifies at most one row.
        /// </summary>
        TreeNodeKeyPath,

        /// <summary>
        ///     The argument must read no path, so that its value is the same for every row and can
        ///     be computed once before the statement runs.
        /// </summary>
        PathFree,
    }

    /// <summary>A restriction on an argument beyond its type, checked when the call binds.</summary>
    /// <param name="Kind">Which restriction.</param>
    /// <param name="Consumed">
    ///     Whether the value is used up at compile time and never reaches the backend. True for a
    ///     selector that picks an emission; false for a value that merely has to be statically
    ///     known, which still travels as a bound parameter.
    /// </param>
    /// <param name="Values">The accepted values, for a closed set.</param>
    internal sealed record ParameterConstraint(
        ParameterConstraintKind Kind,
        bool Consumed = false,
        FrozenSet<string>? Values = null)
    {
        /// <summary>No restriction.</summary>
        internal static ParameterConstraint None { get; } = new ParameterConstraint(ParameterConstraintKind.None);

        /// <summary>The argument has to be a bare path into a hierarchical entity.</summary>
        internal static ParameterConstraint TreeNodeKeyPath { get; } =
            new ParameterConstraint(ParameterConstraintKind.TreeNodeKeyPath);

        /// <summary>The argument must read no path.</summary>
        internal static ParameterConstraint PathFree { get; } =
            new ParameterConstraint(ParameterConstraintKind.PathFree);

        /// <summary>The argument has to be a literal.</summary>
        /// <param name="consumed">Whether the value is used up at compile time.</param>
        /// <returns>The restriction.</returns>
        internal static ParameterConstraint LiteralOnly(bool consumed)
        {
            return new ParameterConstraint(ParameterConstraintKind.LiteralOnly, consumed);
        }

        /// <summary>The argument has to be a literal drawn from a closed set.</summary>
        /// <param name="values">The accepted values, matched case-insensitively.</param>
        /// <returns>The restriction.</returns>
        internal static ParameterConstraint MemberOf(params string[] values)
        {
            return new ParameterConstraint(
                ParameterConstraintKind.MemberOf,
                Consumed: true,
                values.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>How a signature's result nullity follows from its arguments'.</summary>
    internal enum NullityRuleKind
    {
        /// <summary>Always present, whatever the arguments are. Obliges the emission to be total.</summary>
        AlwaysNotNull,

        /// <summary>Absent when any of the named arguments is absent.</summary>
        Union,

        /// <summary>
        ///     Present only when the argument is, no per-row condition was given, and the query
        ///     groups by something.
        /// </summary>
        Aggregate,

        /// <summary>Always present: counting an empty set yields a number.</summary>
        CountAggregate,

        /// <summary>Present when both branches are; absent when both are.</summary>
        Conditional,

        /// <summary>Present when any argument is; absent when all are.</summary>
        Coalesce,

        /// <summary>Present only when the execution has a signed-in user.</summary>
        ContextUser,

        /// <summary>A fixed answer.</summary>
        Constant,
    }

    /// <summary>How a signature's result nullity is decided.</summary>
    /// <param name="Kind">Which rule.</param>
    /// <param name="Indices">The argument positions the rule reads.</param>
    /// <param name="Fixed">The fixed answer, for a constant rule.</param>
    internal readonly record struct NullityRule(
        NullityRuleKind Kind,
        ImmutableArray<int> Indices,
        QueryexNullity Fixed = QueryexNullity.NotNull)
    {
        /// <summary>Always present.</summary>
        internal static NullityRule AlwaysNotNull { get; } = new NullityRule(NullityRuleKind.AlwaysNotNull, []);

        /// <summary>An aggregate's rule.</summary>
        internal static NullityRule Aggregate { get; } = new NullityRule(NullityRuleKind.Aggregate, []);

        /// <summary>A count's rule.</summary>
        internal static NullityRule CountAggregate { get; } = new NullityRule(NullityRuleKind.CountAggregate, []);

        /// <summary>A conditional's rule.</summary>
        internal static NullityRule Conditional { get; } = new NullityRule(NullityRuleKind.Conditional, []);

        /// <summary>A first-present-wins rule.</summary>
        internal static NullityRule Coalesce { get; } = new NullityRule(NullityRuleKind.Coalesce, []);

        /// <summary>The current user's rule.</summary>
        internal static NullityRule ContextUser { get; } = new NullityRule(NullityRuleKind.ContextUser, []);

        /// <summary>Absent when any of the named arguments is absent.</summary>
        /// <param name="indices">The argument positions to read.</param>
        /// <returns>The rule.</returns>
        internal static NullityRule Union(params int[] indices)
        {
            return new NullityRule(NullityRuleKind.Union, [.. indices]);
        }

        /// <summary>Absent exactly when the named argument is.</summary>
        /// <param name="index">The argument position to read.</param>
        /// <returns>The rule.</returns>
        internal static NullityRule Propagate(int index)
        {
            return new NullityRule(NullityRuleKind.Union, [index]);
        }

        /// <summary>A fixed answer.</summary>
        /// <param name="nullity">The answer.</param>
        /// <returns>The rule.</returns>
        internal static NullityRule Always(QueryexNullity nullity)
        {
            return new NullityRule(NullityRuleKind.Constant, [], nullity);
        }
    }

    /// <summary>One declared parameter of one overload.</summary>
    /// <param name="Name">The parameter's name, for diagnostics.</param>
    /// <param name="Type">The type it accepts.</param>
    internal sealed record FunctionParameter(string Name, TypeSpec Type)
    {
        /// <summary>
        ///     Whether this is a variadic tail. Must be a signature's last parameter, and matches one
        ///     or more trailing arguments, each checked against this parameter.
        /// </summary>
        internal bool Rest { get; init; }

        /// <summary>
        ///     Whether a demand on the call propagates into this argument rather than the argument
        ///     deciding its own type.
        /// </summary>
        /// <remarks>
        ///     What makes a conditional or a first-present-wins call usable where a specific type is
        ///     wanted: the demand reaches the branches, so an absent-value literal or a date written
        ///     as text inside one is read at the type the surrounding expression needs. Declared
        ///     here rather than special-cased in the binder.
        /// </remarks>
        internal bool Propagates { get; init; }

        /// <summary>What else the argument has to satisfy.</summary>
        internal ParameterConstraint Constraint { get; init; } = ParameterConstraint.None;
    }

    /// <summary>
    ///     Extra arguments the engine supplies itself, appended after the declared ones.
    /// </summary>
    /// <param name="Origin">Where the value comes from.</param>
    /// <param name="Type">Its type.</param>
    /// <param name="FromParameter">
    ///     The declared parameter whose consumed literal produces the value, or null when the host
    ///     supplies it directly.
    /// </param>
    internal sealed record SyntheticArgument(
        QueryexParameterOrigin Origin,
        BoundType Type,
        int? FromParameter = null);

    /// <summary>One overload of one function.</summary>
    internal sealed record FunctionSignature
    {
        /// <summary>The declared parameters, in order.</summary>
        internal required ImmutableArray<FunctionParameter> Parameters { get; init; }

        /// <summary>The result type.</summary>
        internal required TypeSpec Returns { get; init; }

        /// <summary>How the result's nullity follows from the arguments'.</summary>
        internal required NullityRule Nullity { get; init; }

        /// <summary>How the call becomes SQL.</summary>
        internal required EmitStrategy Emit { get; init; }

        /// <summary>Extra arguments the engine supplies itself.</summary>
        internal ImmutableArray<SyntheticArgument> Synthetic { get; init; } = [];

        /// <summary>
        ///     The fewest arguments this overload accepts. A variadic tail matches one or more, so
        ///     it counts as one either way.
        /// </summary>
        internal int MinArity => Parameters.Length;

        /// <summary>The most arguments this overload accepts.</summary>
        internal int MaxArity =>
            Parameters.Length > 0 && Parameters[^1].Rest ? int.MaxValue : Parameters.Length;

        /// <summary>
        ///     Whether a demand on the call reaches its arguments, which makes the call itself
        ///     usable wherever a specific type is wanted.
        /// </summary>
        internal bool IsCheckable => Parameters.Any(static parameter => parameter.Propagates);

        /// <summary>Renders the signature, for a no-overload-matched diagnostic.</summary>
        /// <param name="name">The function's name.</param>
        /// <returns>The rendered signature.</returns>
        internal string Describe(string name)
        {
            IEnumerable<string> parameters = Parameters.Select(static parameter =>
                parameter.Rest
                    ? parameter.Name + "..."
                    : parameter.Name);

            return name + "(" + string.Join(", ", parameters) + ")";
        }
    }

    /// <summary>
    ///     One function name and every overload it has.
    /// </summary>
    /// <remarks>
    ///     Functions are declarations rather than code paths through the binder, which is what makes
    ///     adding one a registry entry and an emission, and nothing else.
    /// </remarks>
    /// <param name="Name">The name, matched case-insensitively.</param>
    /// <param name="Category">Whether it reads one row or a whole group.</param>
    /// <param name="Signatures">The overloads.</param>
    internal sealed record FunctionDefinition(
        string Name,
        FunctionCategory Category,
        ImmutableArray<FunctionSignature> Signatures);
}
