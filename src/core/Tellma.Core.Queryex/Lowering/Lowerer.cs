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

        /// <summary>
        ///     The statement's one-row sources — values given a name and calendar lookups — in the
        ///     order they were made.
        /// </summary>
        private readonly List<LateralSource> _laterals = [];

        /// <summary>The semi-joins' subqueries, in the order they were begun.</summary>
        /// <remarks>
        ///     A subquery is begun before its own predicate is lowered, so this order is the pre-order
        ///     of the statement's subqueries — the order their aliases are numbered in.
        /// </remarks>
        private readonly List<PlanSubquery> _subqueries = [];

        /// <summary>The values computed once before the statement runs.</summary>
        private readonly List<HoistedVariable> _variables = [];

        /// <summary>The variables by what they look up, so equal lookups share one.</summary>
        private readonly Dictionary<VariableKey, HoistedVariable> _variableIndex = [];

        /// <summary>The table-calendar calls hoisted so far, so equal calls share one variable.</summary>
        private readonly List<(TypedCall Call, ComputedVariable Variable)> _calendarVariables = [];

        /// <summary>The lateral sources something that survived still reads.</summary>
        private readonly HashSet<LateralSource> _liveLaterals = new(ReferenceEqualityComparer.Instance);

        /// <summary>The scalar lookups that survived, in the order they were reached.</summary>
        private readonly List<PlanScalarLookup> _liveScalars = [];

        /// <summary>The variables something that survived still reads.</summary>
        private readonly HashSet<HoistedVariable> _liveVariables = new(ReferenceEqualityComparer.Instance);

        /// <summary>The slots something that survived still binds.</summary>
        private readonly HashSet<ParameterSlot> _liveSlots = new(ReferenceEqualityComparer.Instance);

        /// <summary>Where the first surviving reference to each live slot was written.</summary>
        private readonly Dictionary<ParameterSlot, PlanOrigin> _slotsReachedFrom =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>The ceilings, shared with binding.</summary>
        private readonly CompilationBudget _budget;

        /// <summary>Where problems are recorded.</summary>
        private readonly DiagnosticSink _sink;

        /// <summary>The position of this query among the ones a host executes together.</summary>
        private readonly int _batchOrdinal;

        /// <summary>The schema, which names the month table of each table calendar it maps.</summary>
        private readonly QueryexSchema? _schema;

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

        /// <summary>
        ///     The current root: the join that paths being lowered resolve from.
        /// </summary>
        /// <remarks>
        ///     The statement's root, the target of an enclosing rebase, or the root of an enclosing
        ///     semi-join's subquery. A path walks from here through the join tree the current root
        ///     belongs to, so a rebased criterion shares the joins of every other clause. Null while
        ///     lowering a statement that has no row source.
        /// </remarks>
        private JoinNode? _base;

        /// <summary>
        ///     Where a one-row source made here is collected: the statement's sequence, a subquery's,
        ///     or one level of a scalar lookup's.
        /// </summary>
        private List<LateralSource> _scope;

        /// <summary>Initializes a lowering.</summary>
        /// <param name="root">
        ///     The entity the query reads, or null for a statement that has no row source and so
        ///     lowers nothing that reads a row.
        /// </param>
        /// <param name="schema">The schema, which names the month tables of its table calendars.</param>
        /// <param name="batchOrdinal">The query's position among the ones executed together.</param>
        /// <param name="budget">The ceilings, shared with binding.</param>
        /// <param name="sink">Where problems are recorded.</param>
        internal Lowerer(
            EntityDescriptor? root,
            QueryexSchema? schema,
            int batchOrdinal,
            CompilationBudget budget,
            DiagnosticSink sink)
        {
            Root = root is null ? null : new JoinNode(root, null, null, default) { IsReferenced = true };
            _base = Root;
            _scope = _laterals;
            _schema = schema;
            _batchOrdinal = batchOrdinal;
            _budget = budget;
            _sink = sink;
        }

        /// <summary>The root of the join tree, or null for a statement that has no row source.</summary>
        internal JoinNode? Root { get; }

        /// <summary>The current root, for something that reads a row.</summary>
        /// <exception cref="InvalidOperationException">The statement has no row source.</exception>
        private JoinNode Base => _base ?? throw Rootless();

        /// <summary>The values written to the backend, in the order they were assigned.</summary>
        internal IReadOnlyList<ParameterSlot> Slots => _slots;

        /// <summary>The one-row sources applied to each row, in the order they were made.</summary>
        internal IReadOnlyList<LateralSource> Laterals => _laterals;

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

            // Every clause starts at the statement's own root, whatever the last one ended in.
            _base = Root;
            _scope = _laterals;
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
                TypedFilterLeaf leaf => In(leaf.Location, () => LowerPredicate(leaf.Expression)),
                TypedVia via => In(via.Location, () => LowerVia(via)),
                TypedExists exists => In(exists.Location, () => LowerExists(exists)),
                TypedError => throw Unlowerable(node),
                _ => ReadAsPredicate(node),
            };
        }

        /// <summary>Lowers one part of a composed filter in the input it was written in.</summary>
        /// <param name="location">The part's input.</param>
        /// <param name="lower">What lowers it.</param>
        /// <returns>The lowered predicate.</returns>
        /// <remarks>
        ///     Whatever the part creates — joins, a subquery's source, parameters — remembers this
        ///     input, so a ceiling it pushes over names the leaf, rebase or semi-join rather than the
        ///     whole clause.
        /// </remarks>
        private PlanPredicate In(DiagnosticLocation location, Func<PlanPredicate> lower)
        {
            DiagnosticLocation saved = _location;
            _location = location;
            PlanPredicate lowered = lower();
            _location = saved;
            return lowered;
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

        /// <summary>The failure for something that reads a row reaching a statement without one.</summary>
        /// <returns>The exception to throw.</returns>
        /// <remarks>
        ///     Not reachable: a statement with no row source refuses every path while binding, and
        ///     nothing is lowered while the sink holds a diagnostic. Loud for the same reason an
        ///     unbound node is — the alternative is a statement built around a guess.
        /// </remarks>
        private static InvalidOperationException Rootless()
        {
            return new InvalidOperationException(
                "Something that reads a row reached the lowering of a statement that has no row source.");
        }

        /// <summary>Allocates the slot a paging value binds through.</summary>
        /// <param name="value">The value.</param>
        /// <param name="suffix">What follows the batch prefix in the slot's name.</param>
        /// <returns>The slot, already named.</returns>
        /// <remarks>
        ///     <para>
        ///         Bound as a whole number rather than at the language's numeric default: the paging
        ///         clause rejects a fractional count outright.
        ///     </para>
        ///     <para>
        ///         Kept out of the shared table of slots and given a fixed name, rather than the next
        ///         number in sequence and a share in any equal value. A page is then not part of what
        ///         a compilation is: the second page of a query is the first page's SQL with two values
        ///         changed, so the cached template and the backend's cached plan both serve it.
        ///     </para>
        /// </remarks>
        internal ParameterSlot PagingSlot(int value, string suffix)
        {
            return new ParameterSlot(
                BoundType.Numeric,
                QueryexStoreType.QxInt,
                QueryexParameterOrigin.Literal,
                value,
                null,
                new PlanOrigin(default, _location))
            {
                Name = Prefix + "_" + suffix,
            };
        }

        /// <summary>Lowers one key-set restriction into a conjunct of the row filter.</summary>
        /// <param name="navigations">The navigations from the root to the restricted property.</param>
        /// <param name="property">The restricted property.</param>
        /// <param name="tableSource">The host's table of keys.</param>
        /// <returns>The lowered predicate.</returns>
        /// <remarks>
        ///     The navigations are walked through the shared join tree like any path a clause wrote,
        ///     so a restriction through a navigation the select list also reads costs no second join.
        /// </remarks>
        internal PlanKeySetRestriction LowerRestriction(
            ImmutableArray<NavigationDescriptor> navigations,
            PropertyDescriptor property,
            string tableSource)
        {
            JoinNode table = Walk(navigations, default);
            return new PlanKeySetRestriction(new PlanColumn(default, table, property, Here(default)), tableSource);
        }

        /// <summary>Reads a column of the root that the schema leaves out.</summary>
        /// <param name="column">The physical column name, unquoted.</param>
        /// <returns>The lowered value.</returns>
        internal PlanUndeclaredColumn UndeclaredColumn(string column)
        {
            return new PlanUndeclaredColumn(Root ?? throw Rootless(), column);
        }

        /// <summary>
        ///     Settles the plan's shared parts: drops everything nothing reads any more, and gives
        ///     every surviving join, binding, variable, and slot the name it is written under.
        /// </summary>
        /// <param name="fragments">Every fragment the statement will actually contain.</param>
        /// <param name="paging">
        ///     The slots the paging clause binds, which no fragment mentions and which are named
        ///     already.
        /// </param>
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
            IReadOnlyList<ParameterSlot> paging)
        {
            Reach(fragments);

            ImmutableArray<JoinNode>.Builder joins = ImmutableArray.CreateBuilder<JoinNode>();
            int alias = 0;
            foreach (JoinNode node in Root?.Walk() ?? [])
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

            // The subqueries that survived, numbered in pre-order. Each one's joins and bindings take
            // the next numbers of the statement's own sequences, so no alias inside a subquery can
            // shadow one outside it — the correlation reads the outer row by its alias.
            List<PlanSubquery> subqueries = [.. _subqueries.Where(subquery => subquery.IsReferenced)];
            for (int index = 0; index < subqueries.Count; index++)
            {
                PlanSubquery subquery = subqueries[index];
                subquery.Root.Alias = "X" + (index + 1).ToString(CultureInfo.InvariantCulture);
                subquery.Joins = Aliased(subquery.Root, ref alias);
            }

            _laterals.RemoveAll(lateral => !_liveLaterals.Contains(lateral));
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
                Consume(join);
            }

            // A subquery's source is one more table the statement reads, and its joins are joins like
            // any other; counting them is what keeps a semi-join from being a way around the ceiling.
            foreach (PlanSubquery subquery in subqueries)
            {
                _budget.TryConsumeJoin(subquery.Origin.Span, _sink.Scope(subquery.Origin.Location));
                foreach (JoinNode join in subquery.Joins)
                {
                    Consume(join);
                }
            }

            // The paging slots count too: they are parameters of the statement like any other, and
            // only their names and their place at the end of the list set them apart.
            foreach (ParameterSlot slot in _slots.Concat(paging))
            {
                // Named for the first surviving reference to it; the paging slots, which no fragment
                // mentions, for where they were made.
                PlanOrigin origin = _slotsReachedFrom.GetValueOrDefault(slot, slot.Written);
                _budget.TryConsumeParameter(origin.Span, _sink.Scope(origin.Location));
            }

            // Bindings and lookups each have a sequence of their own, numbered in the order they
            // were made: the statement's first, then subquery by subquery in pre-order.
            int binding = 0;
            int lookup = 0;
            NameLaterals(_laterals, ref binding, ref lookup);
            foreach (PlanSubquery subquery in subqueries)
            {
                ImmutableArray<LateralSource> live = [.. subquery.Pending.Where(_liveLaterals.Contains)];
                NameLaterals(live, ref binding, ref lookup);
                subquery.Laterals = live;
            }

            // A scalar lookup is a subquery of its own, so its names restart with each one: its
            // levels are M, M2, and so on, outermost first, and the values named inside it are V1,
            // V2, and so on, in the order they are written — the innermost level's first, since each
            // level's source follows the levels nested in its select list. A family of their own
            // keeps them from shadowing a binding of the statement that an operand reads.
            foreach (PlanScalarLookup scalar in _liveScalars)
            {
                for (int level = 0; level < scalar.Levels.Length; level++)
                {
                    scalar.Levels[level].Alias = level == 0
                        ? ScalarLevelAlias
                        : ScalarLevelAlias + (level + 1).ToString(CultureInfo.InvariantCulture);
                }

                int value = 0;
                for (int level = scalar.Levels.Length - 1; level >= 0; level--)
                {
                    CalendarLookup current = scalar.Levels[level];
                    current.Bindings.RemoveAll(named => !_liveLaterals.Contains(named));
                    foreach (ValueBinding named in current.Bindings)
                    {
                        named.Alias = ScalarValueAlias + (++value).ToString(CultureInfo.InvariantCulture);
                    }
                }
            }

            for (int index = 0; index < _variables.Count; index++)
            {
                _variables[index].Name =
                    Prefix + "_v" + index.ToString(CultureInfo.InvariantCulture);
            }

            for (int index = 0; index < _slots.Count; index++)
            {
                _slots[index].Name =
                    Prefix + "_p" + index.ToString(CultureInfo.InvariantCulture);
            }

            return joins.ToImmutable();
        }

        /// <summary>The alias of a scalar lookup's outermost level; the deeper ones are numbered from 2.</summary>
        private const string ScalarLevelAlias = "M";

        /// <summary>The alias family of the values named inside a scalar lookup.</summary>
        private const string ScalarValueAlias = "V";

        /// <summary>Names one sequence of surviving lateral sources.</summary>
        /// <param name="laterals">The sources, in the order they were made.</param>
        /// <param name="binding">The last binding number used, advanced past the ones assigned here.</param>
        /// <param name="lookup">The last lookup number used, advanced past the ones assigned here.</param>
        private static void NameLaterals(IEnumerable<LateralSource> laterals, ref int binding, ref int lookup)
        {
            foreach (LateralSource lateral in laterals)
            {
                lateral.Alias = lateral is CalendarLookup
                    ? "C" + (++lookup).ToString(CultureInfo.InvariantCulture)
                    : "B" + (++binding).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Counts one surviving join toward the ceiling.</summary>
        /// <param name="join">The join.</param>
        /// <remarks>
        ///     Named for the first surviving reference that reads it rather than for whatever created
        ///     it, which may have folded away: a join that only a select item still reads is the select
        ///     item's to answer for.
        /// </remarks>
        private void Consume(JoinNode join)
        {
            PlanOrigin origin = join.ReachedFrom ?? join.Origin;
            _budget.TryConsumeJoin(origin.Span, _sink.Scope(origin.Location));
        }

        /// <summary>Names every surviving join of one join tree, parents first.</summary>
        /// <param name="root">The tree's root, which is named already.</param>
        /// <param name="alias">The last join number used, advanced past the ones assigned here.</param>
        /// <returns>The surviving joins, in alias order and excluding the root.</returns>
        private static ImmutableArray<JoinNode> Aliased(JoinNode root, ref int alias)
        {
            ImmutableArray<JoinNode>.Builder joins = ImmutableArray.CreateBuilder<JoinNode>();
            foreach (JoinNode node in root.Walk())
            {
                // The root is the subquery's source, and an unread node has only unread nodes below it.
                if (node.Parent is null || !node.IsReferenced)
                {
                    continue;
                }

                alias++;
                node.Alias = "P" + alias.ToString(CultureInfo.InvariantCulture);
                joins.Add(node);
            }

            return joins.ToImmutable();
        }

        /// <summary>The prefix every name this query emits begins with.</summary>
        /// <remarks>
        ///     The engine's reserved namespace, then the query's position among those a host executes
        ///     together, so that several compiled queries compose into one batch without colliding.
        /// </remarks>
        private string Prefix => "@qx" + _batchOrdinal.ToString(CultureInfo.InvariantCulture);

        /// <summary>Finds everything the finished fragments actually read.</summary>
        /// <param name="fragments">
        ///     Every fragment the statement will contain, in the order they were lowered.
        /// </param>
        /// <remarks>
        ///     Depth first, in the order things were written, so the first surviving reference to a
        ///     join or a slot is the one a ceiling it pushes over is reported against — the same one
        ///     that created it, unless that one folded away.
        /// </remarks>
        private void Reach(IEnumerable<PlanNode> fragments)
        {
            Stack<PlanNode> pending = new(fragments.Reverse());
            HashSet<PlanNode> seen = new(ReferenceEqualityComparer.Instance);

            while (pending.Count > 0)
            {
                PlanNode node = pending.Pop();
                if (!seen.Add(node))
                {
                    continue;
                }

                // Pushed last-first, so they are visited in the order they are written; whatever a
                // binding or a variable reads is pushed after them, so it is visited first.
                ImmutableArray<PlanNode> children = PlanWalk.Children(node);
                for (int index = children.Length - 1; index >= 0; index--)
                {
                    pending.Push(children[index]);
                }

                switch (node)
                {
                    case PlanColumn column:
                        column.Table.MarkReferenced(column.Origin);
                        break;

                    case PlanParameterRef parameter:
                        if (_liveSlots.Add(parameter.Slot))
                        {
                            _slotsReachedFrom[parameter.Slot] = parameter.Origin;
                        }

                        break;

                    case PlanBindingRef binding:

                        // What a binding computes is read too, transitively: a binding referenced
                        // from a surviving fragment keeps alive whatever it names.
                        if (_liveLaterals.Add(binding.Binding))
                        {
                            pending.Push(binding.Binding.Value);
                        }

                        break;

                    case PlanLookupColumn column:

                        // The same for a lookup: whatever it is sought by — a date, or another
                        // lookup's year and month — is read whenever a column of it is.
                        if (_liveLaterals.Add(column.Lookup))
                        {
                            foreach (PlanNode read in column.Lookup.Reads)
                            {
                                pending.Push(read);
                            }
                        }

                        break;

                    case PlanScalarLookup scalar:

                        // A level's bindings are named with the statement's, once it is known which
                        // levels survived; the level reads them through its own references.
                        _liveScalars.Add(scalar);
                        break;

                    case PlanExists exists:

                        // A subquery is part of the statement exactly when its semi-join is.
                        exists.Subquery.IsReferenced = true;
                        break;

                    case PlanVariableRef variable:
                        if (_liveVariables.Add(variable.Variable))
                        {
                            // A variable is computed before the statement rather than against a
                            // join, so only what its initializer reads — a key, or a value that
                            // reads no row — can reach anything else the statement holds.
                            pending.Push(variable.Variable.Reads);
                        }

                        break;

                    default:
                        break;
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
