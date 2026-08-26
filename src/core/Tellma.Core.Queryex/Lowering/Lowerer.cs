// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Globalization;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Emit;
using Tellma.Core.Queryex.Nullity;
using static System.FormattableString;

namespace Tellma.Core.Queryex.Lowering
{
    /// <summary>
    ///     Turns bound trees into a plan the writer can transcribe without deciding anything.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every clause of one query is lowered through one instance, so they share one join
    ///         tree, one alias sequence, one parameter table, and one set of bindings. A navigation
    ///         two clauses mention becomes one join, and a value two clauses compare against becomes
    ///         one parameter.
    ///     </para>
    ///     <para>
    ///         This is where the guards that make comparison total are put in, where operands that
    ///         would otherwise be written twice are given names, and where anything already known to
    ///         be absent collapses to a constant — which is also what prunes the joins nothing reads
    ///         any more.
    ///     </para>
    /// </remarks>
    internal sealed partial class Lowerer
    {
        /// <summary>The values written to the backend, in the order they were assigned.</summary>
        private readonly List<ParameterSlot> _slots = [];

        /// <summary>The slots by what they carry, so equal values share one.</summary>
        private readonly Dictionary<SlotKey, ParameterSlot> _slotIndex = [];

        /// <summary>The values computed once per row and given a name.</summary>
        private readonly List<ValueBinding> _bindings = [];

        /// <summary>The values computed once before the statement runs.</summary>
        private readonly List<HoistedVariable> _variables = [];

        /// <summary>The variables by what they look up, so equal lookups share one.</summary>
        private readonly Dictionary<VariableKey, HoistedVariable> _variableIndex = [];

        /// <summary>The bindings something that survived still reads.</summary>
        private readonly HashSet<ValueBinding> _liveBindings = new(ReferenceEqualityComparer.Instance);

        /// <summary>The variables something that survived still reads.</summary>
        private readonly HashSet<HoistedVariable> _liveVariables = new(ReferenceEqualityComparer.Instance);

        /// <summary>The slots something that survived still binds.</summary>
        private readonly HashSet<ParameterSlot> _liveSlots = new(ReferenceEqualityComparer.Instance);

        /// <summary>The ceilings, shared with binding.</summary>
        private readonly CompilationBudget _budget;

        /// <summary>Where problems are recorded.</summary>
        private readonly DiagnosticSink _sink;

        /// <summary>The position of this query among the ones a host executes together.</summary>
        private readonly int _batchOrdinal;

        /// <summary>The nullity answers for the clause being lowered.</summary>
        private NullityMap _nullity = NullityMap.Empty;

        /// <summary>Which input the clause being lowered is, and so where its problems go.</summary>
        private DiagnosticLocation _location = DiagnosticLocation.Clause(QueryexClause.Filter);

        /// <summary>Whether a value here may be given a name rather than written again.</summary>
        private bool _bindingsAllowed = true;

        /// <summary>
        ///     Whether a subexpression whose answer is already known may be replaced by that answer.
        /// </summary>
        /// <remarks>
        ///     Folding is an optimization everywhere but in a grouping key. There the backend
        ///     requires every expression to read at least one column, and a key that folded to a
        ///     constant would be refused outright — so keys are lowered in the general form, which
        ///     always mentions what it reads.
        /// </remarks>
        private bool _folding = true;

        /// <summary>The column the value being lowered is compared against, when there is one.</summary>
        private QueryexStoreType? _hint;

        /// <summary>Initializes a lowering.</summary>
        /// <param name="root">The entity the query reads.</param>
        /// <param name="batchOrdinal">The query's position among the ones executed together.</param>
        /// <param name="budget">The ceilings, shared with binding.</param>
        /// <param name="sink">Where problems are recorded.</param>
        internal Lowerer(
            EntityDescriptor root,
            int batchOrdinal,
            CompilationBudget budget,
            DiagnosticSink sink)
        {
            Root = new JoinNode(root, null, null, default) { IsReferenced = true };
            _batchOrdinal = batchOrdinal;
            _budget = budget;
            _sink = sink;
        }

        /// <summary>The root of the join tree.</summary>
        internal JoinNode Root { get; }

        /// <summary>The values written to the backend, in the order they were assigned.</summary>
        internal IReadOnlyList<ParameterSlot> Slots => _slots;

        /// <summary>The values computed once per row and given a name.</summary>
        internal IReadOnlyList<ValueBinding> Bindings => _bindings;

        /// <summary>The values computed once before the statement runs.</summary>
        internal IReadOnlyList<HoistedVariable> Variables => _variables;

        /// <summary>Starts lowering one clause, or one item of one.</summary>
        /// <param name="nullity">The nullity answers for the clause's bound tree.</param>
        /// <param name="location">Which input the clause is.</param>
        /// <param name="bindingsAllowed">
        ///     Whether a lateral binding is referenceable from here. It is wherever the fragment
        ///     sits at row grain: everywhere in an ungrouped query, and in a grouped one only within
        ///     an aggregate's arguments and in the items that become the grouping keys.
        /// </param>
        /// <param name="folding">
        ///     Whether a subexpression whose answer is already known may be replaced by that answer.
        ///     Off for a grouping key, which has to keep reading the columns it groups by.
        /// </param>
        internal void Begin(
            NullityMap nullity,
            DiagnosticLocation location,
            bool bindingsAllowed,
            bool folding = true)
        {
            _nullity = nullity;
            _location = location;
            _bindingsAllowed = bindingsAllowed;
            _folding = folding;
            _hint = null;
        }

        /// <summary>Lowers one expression consumed as a scalar.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The lowered value.</returns>
        internal PlanValue LowerValue(TypedExpr node)
        {
            return node switch
            {
                TypedLiteral literal => LowerLiteral(literal),
                TypedNull => new PlanNullValue(node.Span, node.Type),
                TypedPath path => LowerPath(path),
                TypedParameter parameter => LowerParameter(parameter),
                TypedNegate negate => new PlanNegate(node.Span, LowerValue(negate.Operand)),
                TypedArithmetic arithmetic => new PlanArithmetic(
                    node.Span,
                    arithmetic.Operator,
                    LowerValue(arithmetic.Left),
                    LowerValue(arithmetic.Right)),
                TypedCall call => LowerCallValue(call),

                // The one node kind neither this side nor the predicate side can lower. It is not
                // reachable: a tree that failed to bind carries a diagnostic, and nothing is lowered
                // while the sink holds one. Named anyway, because the two sides fall through to each
                // other, so an unnamed kind is not a missing case but a non-terminating one.
                TypedError => throw Unlowerable(node),

                // Everything left produces a truth value, and reading one as a scalar is the one
                // thing the language's single boolean type costs: it has to be turned into a number
                // where a number is what the position wants.
                _ => new PlanValueOfPredicate(node.Span, LowerPredicate(node)),
            };
        }

        /// <summary>Lowers one expression consumed as a truth value.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The lowered predicate.</returns>
        internal PlanPredicate LowerPredicate(TypedExpr node)
        {
            return node switch
            {
                TypedLiteral { Value: bool constant } => new PlanConstantPredicate(node.Span, constant),

                // An absent truth value reads as false, which is what keeps a negation of it from
                // turning into something a reader would not predict.
                TypedNull => new PlanConstantPredicate(node.Span, false),

                TypedLogical logical => LowerLogical(logical),
                TypedNot not => Negate(LowerPredicate(not.Operand), node.Span),
                TypedComparison comparison => LowerComparison(comparison),
                TypedIn membership => LowerIn(membership),
                TypedIsNull test => LowerIsNull(test),
                TypedCall call when call.Signature.Emit.ResultShape == EmitShape.Predicate =>
                    LowerCallPredicate(call),
                TypedError => throw Unlowerable(node),
                _ => ReadAsPredicate(node),
            };
        }

        /// <summary>The failure for a node that should never have reached lowering.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The exception to throw.</returns>
        /// <remarks>
        ///     Loud rather than quiet. The alternatives are worse in both directions: a statement
        ///     built around a guess at what the author meant, on a surface that decides who may read
        ///     what, or a walk that never returns.
        /// </remarks>
        private static InvalidOperationException Unlowerable(TypedExpr node)
        {
            return new InvalidOperationException(
                Invariant($"An expression that did not bind reached lowering at {node.Span.Start}."));
        }

        /// <summary>Allocates the slot a paging value binds through.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The slot.</returns>
        /// <remarks>
        ///     Bound as a whole number rather than at the language's numeric default: the paging
        ///     clause rejects a fractional count outright.
        /// </remarks>
        internal ParameterSlot PagingSlot(int value)
        {
            return Slot(
                BoundType.Numeric,
                QueryexStoreType.QxInt,
                QueryexParameterOrigin.Literal,
                value,
                declaredName: null,
                default);
        }

        /// <summary>
        ///     Settles the plan's shared parts: drops everything nothing reads any more, and gives
        ///     every surviving join, binding, variable, and slot the name it is written under.
        /// </summary>
        /// <param name="fragments">Every fragment the statement will actually contain.</param>
        /// <param name="paging">The slots the paging clause binds, which no fragment mentions.</param>
        /// <returns>The surviving joins, in alias order and excluding the root.</returns>
        /// <remarks>
        ///     What survives is read off the finished fragments rather than noted while they were
        ///     built. A subexpression that folded away had already claimed its joins, its bindings
        ///     and its parameters by the time it folded, and keeping those would leave a statement
        ///     joining a table it never reads — which, on a mandatory navigation, quietly drops the
        ///     rows whose row on the other side is missing.
        /// </remarks>
        internal ImmutableArray<JoinNode> Complete(
            IEnumerable<PlanNode> fragments,
            IEnumerable<ParameterSlot> paging)
        {
            Reach(fragments, paging);

            ImmutableArray<JoinNode>.Builder joins = ImmutableArray.CreateBuilder<JoinNode>();
            int alias = 0;
            foreach (JoinNode node in Root.Walk())
            {
                if (!node.IsReferenced)
                {
                    // Everything below an unread node is unread too: a child marks its whole chain
                    // of parents, so a marked child could not sit under an unmarked parent.
                    continue;
                }

                if (node.Parent is null)
                {
                    node.Alias = "T";
                    continue;
                }

                alias++;
                node.Alias = "P" + alias.ToString(CultureInfo.InvariantCulture);
                joins.Add(node);
            }

            _bindings.RemoveAll(binding => !_liveBindings.Contains(binding));
            _variables.RemoveAll(variable => !_liveVariables.Contains(variable));
            _slots.RemoveAll(slot => !_liveSlots.Contains(slot));

            // Both ceilings are counted here, over what the statement will contain, rather than
            // where each was created. They exist to bound the statement, and a query refused over
            // joins that folded away and were never emitted would be refused for nothing — which on
            // this surface means an access-control criterion that grants everything costing the
            // reader the query they were entitled to. The diagnostic still names what pushed the
            // count over, because each of these remembers where it was written.
            foreach (JoinNode join in joins)
            {
                _budget.TryConsumeJoin(join.Origin.Span, _sink.Scope(join.Origin.Location));
            }

            foreach (ParameterSlot slot in _slots)
            {
                _budget.TryConsumeParameter(slot.Written.Span, _sink.Scope(slot.Written.Location));
            }

            for (int index = 0; index < _bindings.Count; index++)
            {
                _bindings[index].Alias =
                    "B" + (index + 1).ToString(CultureInfo.InvariantCulture);
            }

            string prefix = "@qx" + _batchOrdinal.ToString(CultureInfo.InvariantCulture);
            for (int index = 0; index < _variables.Count; index++)
            {
                _variables[index].Name =
                    prefix + "_v" + index.ToString(CultureInfo.InvariantCulture);
            }

            for (int index = 0; index < _slots.Count; index++)
            {
                _slots[index].Name =
                    prefix + "_p" + index.ToString(CultureInfo.InvariantCulture);
            }

            return joins.ToImmutable();
        }

        /// <summary>Finds everything the finished fragments actually read.</summary>
        /// <param name="fragments">Every fragment the statement will contain.</param>
        /// <param name="paging">The slots the paging clause binds.</param>
        private void Reach(IEnumerable<PlanNode> fragments, IEnumerable<ParameterSlot> paging)
        {
            foreach (ParameterSlot slot in paging)
            {
                _liveSlots.Add(slot);
            }

            Queue<PlanNode> pending = new(fragments);
            HashSet<PlanNode> seen = new(ReferenceEqualityComparer.Instance);

            while (pending.Count > 0)
            {
                PlanNode node = pending.Dequeue();
                if (!seen.Add(node))
                {
                    continue;
                }

                switch (node)
                {
                    case PlanColumn column:
                        column.Table.MarkReferenced();
                        break;

                    case PlanParameterRef parameter:
                        _liveSlots.Add(parameter.Slot);
                        break;

                    case PlanBindingRef binding:

                        // What a binding computes is read too, transitively: a binding referenced
                        // from a surviving fragment keeps alive whatever it names.
                        if (_liveBindings.Add(binding.Binding))
                        {
                            pending.Enqueue(binding.Binding.Value);
                        }

                        break;

                    case PlanVariableRef variable:
                        if (_liveVariables.Add(variable.Variable))
                        {
                            // A variable is computed against its own table rather than a join, so
                            // only the key it looks up can reach back into the join tree.
                            pending.Enqueue(variable.Variable.Key);
                        }

                        break;

                    default:
                        break;
                }

                foreach (PlanNode child in PlanWalk.Children(node))
                {
                    pending.Enqueue(child);
                }
            }
        }

        /// <summary>Reads a scalar as a truth value, with whatever guard its nullity calls for.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanPredicate ReadAsPredicate(TypedExpr node)
        {
            QueryexNullity nullity = _nullity[node];
            if (nullity == QueryexNullity.Null && _folding)
            {
                return new PlanConstantPredicate(node.Span, false);
            }

            PlanValue value = LowerValue(node);
            if (nullity == QueryexNullity.Null)
            {
                // Absence read as falsehood, written out rather than folded, so the value it reads
                // is still named.
                return new PlanPredicateOfValue(node.Span, value, PlanBoolGuard.SubstituteAbsent);
            }

            if (nullity == QueryexNullity.NotNull)
            {
                return new PlanPredicateOfValue(node.Span, value, PlanBoolGuard.None);
            }

            // Testing for presence writes the value twice, which is worth it exactly when writing it
            // twice is free — and that is also the case where it buys something, since a bare column
            // comparison is the shape the backend can seek on.
            return new PlanPredicateOfValue(
                node.Span,
                value,
                value.IsAtomic ? PlanBoolGuard.TestPresence : PlanBoolGuard.SubstituteAbsent);
        }

        /// <summary>Lowers a conjunction or a disjunction, folding what is already known.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanPredicate LowerLogical(TypedLogical node)
        {
            bool conjunction = node.Operator == LogicalOperator.And;
            List<PlanPredicate> operands = [];

            foreach (TypedExpr operand in node.Operands)
            {
                PlanPredicate lowered = LowerPredicate(operand);
                if (lowered is PlanConstantPredicate constant && _folding)
                {
                    if (constant.Value != conjunction)
                    {
                        // One false in a conjunction, or one true in a disjunction, settles it.
                        // Whatever earlier operands claimed is released when the plan is finished,
                        // because nothing that survived reads it any more.
                        return constant;
                    }

                    continue;
                }

                operands.Add(lowered);
            }

            return operands.Count switch
            {
                0 => new PlanConstantPredicate(node.Span, conjunction),
                1 => operands[0],
                _ => new PlanJunction(node.Span, conjunction, [.. operands]),
            };
        }

        /// <summary>Negates a predicate, folding what is already known.</summary>
        /// <param name="operand">The predicate.</param>
        /// <param name="span">The range of the negation.</param>
        /// <returns>The lowered predicate.</returns>
        private static PlanPredicate Negate(PlanPredicate operand, QueryexSpan span)
        {
            return operand switch
            {
                PlanConstantPredicate constant => new PlanConstantPredicate(span, !constant.Value),
                PlanNegation negation => negation.Operand,
                _ => new PlanNegation(span, operand),
            };
        }

        /// <summary>Lowers an absence test, folding what the nullity analysis already settled.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanPredicate LowerIsNull(TypedIsNull node)
        {
            QueryexNullity nullity = _nullity[node.Operand];
            if (nullity != QueryexNullity.Nullable && _folding)
            {
                // Asking whether something that is always present is absent has an answer already,
                // and giving it here is what lets the join behind it disappear.
                bool absent = nullity == QueryexNullity.Null;
                return new PlanConstantPredicate(node.Span, absent != node.Negated);
            }

            return new PlanIsNull(node.Span, LowerValue(node.Operand), node.Negated);
        }
    }
}
