// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Functions;

namespace Tellma.Core.Queryex.Nullity
{
    /// <summary>
    ///     Works out, for every bound node, whether it can evaluate to an absent value.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A separate pass over the bound tree rather than a field filled in while binding,
    ///         because the emitter omits guards on the strength of what it concludes, and a claim
    ///         that load-bearing has to be inspectable and testable on its own.
    ///     </para>
    ///     <para>
    ///         The obligation is one-sided. Saying a node may be absent costs a guard and nothing
    ///         else; saying it cannot is a promise that must hold for every database state the
    ///         schema permits. Wherever presence cannot be shown, the answer is that it may be
    ///         absent.
    ///     </para>
    /// </remarks>
    internal sealed class NullityAnalyzer
    {
        /// <summary>Whether execution will have a signed-in user.</summary>
        private readonly bool _hasUser;

        /// <summary>Whether the enclosing query groups by anything.</summary>
        private readonly bool _hasGroupingKeys;

        /// <summary>The answers.</summary>
        private readonly NullityMap _map = new();

        /// <summary>Initializes an analysis.</summary>
        /// <param name="hasUser">Whether execution will have a signed-in user.</param>
        /// <param name="hasGroupingKeys">Whether the enclosing query groups by anything.</param>
        internal NullityAnalyzer(bool hasUser, bool hasGroupingKeys)
        {
            _hasUser = hasUser;
            _hasGroupingKeys = hasGroupingKeys;
        }

        /// <summary>Annotates a whole list of bound items.</summary>
        /// <param name="items">The items.</param>
        /// <returns>The answers.</returns>
        internal NullityMap Analyze(ImmutableArray<BoundItem> items)
        {
            foreach (BoundItem item in items)
            {
                Visit(item.Expression);
            }

            return _map;
        }

        /// <summary>Annotates one node and everything under it.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its nullity.</returns>
        private QueryexNullity Visit(TypedExpr node)
        {
            foreach (TypedExpr child in node.Children)
            {
                Visit(child);
            }

            QueryexNullity nullity = Compute(node);
            _map.Set(node, nullity);
            return nullity;
        }

        /// <summary>Works out one node's nullity from its children's.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its nullity.</returns>
        private QueryexNullity Compute(TypedExpr node)
        {
            return node switch
            {
                TypedLiteral => QueryexNullity.NotNull,
                TypedNull => QueryexNullity.Null,
                TypedPath path => PathNullity(path),
                TypedParameter parameter => parameter.Symbol.Nullity,
                TypedNegate negate => _map[negate.Operand],

                // Absence propagates through arithmetic and through joining text alike: joining an
                // absent value to something yields an absent value rather than treating it as empty
                // text, which is the behaviour a reader expects and the one that composes.
                TypedArithmetic arithmetic =>
                    NullityLattice.Union(_map[arithmetic.Left], _map[arithmetic.Right]),

                // Every construct that yields a truth value yields one: comparison is defined for
                // absent operands rather than undefined by them, so a predicate is never anything
                // but true or false and no third possibility leaks through a negation.
                TypedComparison or TypedIn or TypedIsNull or TypedLogical or TypedNot =>
                    QueryexNullity.NotNull,

                TypedCall call => CallNullity(call),
                _ => QueryexNullity.Nullable,
            };
        }

        /// <summary>Whether a path can evaluate to an absent value.</summary>
        /// <param name="path">The path.</param>
        /// <returns>Its nullity.</returns>
        /// <remarks>
        ///     Present only when every step of the way is: a navigation whose foreign key may be
        ///     absent means there may be no row to read the next name from at all.
        /// </remarks>
        private static QueryexNullity PathNullity(TypedPath path)
        {
            foreach (NavigationDescriptor navigation in path.Navigations)
            {
                if (!navigation.ForeignKey.IsNotNull)
                {
                    return QueryexNullity.Nullable;
                }
            }

            return path.Property.IsNotNull ? QueryexNullity.NotNull : QueryexNullity.Nullable;
        }

        /// <summary>Whether a call can evaluate to an absent value.</summary>
        /// <param name="call">The call.</param>
        /// <returns>Its nullity.</returns>
        private QueryexNullity CallNullity(TypedCall call)
        {
            NullityRule rule = call.Signature.Nullity;
            return rule.Kind switch
            {
                NullityRuleKind.AlwaysNotNull or NullityRuleKind.CountAggregate => QueryexNullity.NotNull,
                NullityRuleKind.Constant => rule.Fixed,

                // Without a signed-in user there is no identifier to compare against, which makes a
                // filter on it provably false rather than accidentally true.
                NullityRuleKind.ContextUser => _hasUser ? QueryexNullity.NotNull : QueryexNullity.Null,
                NullityRuleKind.Union => UnionOf(call, rule.Indices),
                NullityRuleKind.Aggregate => AggregateNullity(call),
                NullityRuleKind.Conditional => ConditionalNullity(call),
                NullityRuleKind.Coalesce => CoalesceNullity(call),
                _ => QueryexNullity.Nullable,
            };
        }

        /// <summary>Widens across the arguments a rule names.</summary>
        /// <param name="call">The call.</param>
        /// <param name="indices">The parameter positions to read.</param>
        /// <returns>The widest answer among them.</returns>
        private QueryexNullity UnionOf(TypedCall call, ImmutableArray<int> indices)
        {
            // Collected and then folded, rather than folded into a starting answer: seeding with
            // "certainly present" would turn a single argument that is certainly absent into merely
            // possibly absent, and the two are different claims.
            List<QueryexNullity> answers = [];
            foreach (int index in indices)
            {
                if (call.RestStart >= 0 && index == call.RestStart)
                {
                    for (int argument = call.RestStart; argument < call.Arguments.Length; argument++)
                    {
                        answers.Add(_map[call.Arguments[argument]]);
                    }

                    continue;
                }

                if (index < call.Arguments.Length)
                {
                    answers.Add(_map[call.Arguments[index]]);
                }
            }

            return NullityLattice.Union(answers);
        }

        /// <summary>Whether an aggregation can evaluate to an absent value.</summary>
        /// <param name="call">The call.</param>
        /// <returns>Its nullity.</returns>
        /// <remarks>
        ///     Two ordinary situations produce no value at all. A filtered aggregate over a group in
        ///     which no row satisfies the filter yields nothing even though the group produces a row;
        ///     and an ungrouped aggregate over a filter that matched nothing yields one absent value.
        ///     Both are everyday reporting, and a rule that ignored them would make a negated
        ///     comparison on a measure silently drop rows.
        /// </remarks>
        private QueryexNullity AggregateNullity(TypedCall call)
        {
            bool conditional = call.Arguments.Length > 1;
            return conditional || !_hasGroupingKeys || call.Arguments.Length == 0
                ? QueryexNullity.Nullable
                : _map[call.Arguments[0]];
        }

        /// <summary>Whether a conditional can evaluate to an absent value.</summary>
        /// <param name="call">The call.</param>
        /// <returns>Its nullity.</returns>
        private QueryexNullity ConditionalNullity(TypedCall call)
        {
            if (call.Arguments.Length < 3)
            {
                return QueryexNullity.Nullable;
            }

            // A condition that is already known takes the whole answer with it: only one branch can
            // ever run, so the other one's nullity says nothing about the result.
            if (call.Arguments[0] is TypedLiteral { Value: bool constant })
            {
                return _map[call.Arguments[constant ? 1 : 2]];
            }

            QueryexNullity whenTrue = _map[call.Arguments[1]];
            QueryexNullity whenFalse = _map[call.Arguments[2]];
            return NullityLattice.Union(whenTrue, whenFalse);
        }

        /// <summary>Whether a first-present-wins call can evaluate to an absent value.</summary>
        /// <param name="call">The call.</param>
        /// <returns>Its nullity.</returns>
        private QueryexNullity CoalesceNullity(TypedCall call)
        {
            bool allAbsent = true;
            foreach (TypedExpr argument in call.Arguments)
            {
                QueryexNullity nullity = _map[argument];
                if (nullity == QueryexNullity.NotNull)
                {
                    // One argument that is certainly present is enough: nothing after it can be
                    // reached, so nothing after it can make the answer absent.
                    return QueryexNullity.NotNull;
                }

                allAbsent &= nullity == QueryexNullity.Null;
            }

            return allAbsent ? QueryexNullity.Null : QueryexNullity.Nullable;
        }
    }
}
