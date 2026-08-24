// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Emit;
using Tellma.Core.Queryex.Lowering;
using Tellma.Core.Queryex.Nullity;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     The clauses of one query while it is being compiled.
    /// </summary>
    /// <remarks>
    ///     Holds the state the clauses share, which is most of what makes a query more than a set of
    ///     independent expressions: the grouping the select list derives, the keys the ordering is
    ///     checked against, and the one lowering that gives every clause the same joins, the same
    ///     aliases, and the same parameters.
    /// </remarks>
    internal sealed class QueryClauses
    {
        /// <summary>The stage runner, which remembers what it has already done.</summary>
        private readonly ExpressionCompiler _compiler;

        /// <summary>The query.</summary>
        private readonly QuerySpec _spec;

        /// <summary>The compilation options.</summary>
        private readonly QueryCompilationOptions _options;

        /// <summary>Where problems are recorded.</summary>
        private readonly DiagnosticSink _sink;

        /// <summary>The ceilings shared across the compilation.</summary>
        private readonly CompilationBudget _budget;

        /// <summary>Every clause's nullity answers, merged.</summary>
        private readonly NullityMap _nullity;

        /// <summary>The select items that become grouping keys, by position.</summary>
        private readonly List<int> _keys = [];

        /// <summary>The declared parameters, resolved once.</summary>
        private readonly FrozenDictionary<string, ParameterSymbol> _symbols;

        /// <summary>The root of the join tree, once lowering has created it.</summary>
        private JoinNode? _root;

        /// <summary>The bound select list.</summary>
        private BoundExpression _select = new([], NullityMap.Empty, 0, []);

        /// <summary>The bound ordering list.</summary>
        private BoundExpression _orderBy = new([], NullityMap.Empty, 0, []);

        /// <summary>The bound row-level predicate, when there is one.</summary>
        private TypedExpr? _filter;

        /// <summary>The bound group-level predicate, when there is one.</summary>
        private TypedExpr? _having;

        /// <summary>Whether the select list derives at least one grouping key.</summary>
        private bool _hasGroupingKeys;

        /// <summary>Initializes the clauses of one compilation.</summary>
        /// <param name="compiler">The stage runner, which remembers what it has already done.</param>
        /// <param name="spec">The query.</param>
        /// <param name="options">The compilation options.</param>
        /// <param name="sink">Where problems are recorded.</param>
        /// <param name="budget">The ceilings shared across the compilation.</param>
        /// <param name="nullity">Every clause's nullity answers, merged.</param>
        internal QueryClauses(
            ExpressionCompiler compiler,
            QuerySpec spec,
            QueryCompilationOptions options,
            DiagnosticSink sink,
            CompilationBudget budget,
            NullityMap nullity)
        {
            _compiler = compiler;
            _spec = spec;
            _options = options;
            _sink = sink;
            _budget = budget;
            _nullity = nullity;
            _symbols = Symbols.From(options.Parameters);
        }

        /// <summary>Binds every clause and checks the rules that span them.</summary>
        /// <returns>True when nothing was reported.</returns>
        internal bool TryBind()
        {
            // The select list is parsed before anything is bound, because whether the query groups
            // by anything follows from its shape and every other clause is bound against that fact.
            var selectAt = DiagnosticLocation.Clause(QueryexClause.Select);
            if (!_compiler.TryParse(
                _spec.Select,
                _options.Limits,
                _sink,
                selectAt,
                out ExpressionListSyntax? selectSyntax))
            {
                return false;
            }

            _hasGroupingKeys = _spec.Aggregate && QueryCompiler.DerivesGroupingKeys(selectSyntax!);

            BindFilter();
            BindSelect(selectSyntax!, selectAt);
            BindHaving();
            BindOrderBy();

            if (_sink.Count > 0)
            {
                return false;
            }

            DeriveKeys();
            CheckOrdering();
            CheckPaging();
            return _sink.Count == 0;
        }

        /// <summary>Lowers every clause into one plan.</summary>
        /// <returns>The plan.</returns>
        internal RelationalPlan Lower()
        {
            Lowerer lowerer = new(_spec.Root, _options.BatchOrdinal, _budget, _sink);
            _root = lowerer.Root;

            PlanPredicate? where = LowerFilter(lowerer);
            (ImmutableArray<PlanSelectItem> select, ImmutableArray<PlanValue> groupBy) = LowerSelect(lowerer);
            PlanPredicate? having = LowerHaving(lowerer);
            ImmutableArray<PlanOrderItem> orderBy = LowerOrdering(lowerer, select);

            ParameterSlot? skip = _spec.Skip is int rows ? lowerer.PagingSlot(rows) : null;
            ParameterSlot? take = _spec.Take is int count ? lowerer.PagingSlot(count) : null;

            // Everything the statement will contain, so that finishing the plan can tell what is
            // still read from what merely got as far as being built.
            List<PlanNode> fragments = [];
            foreach (PlanSelectItem item in select)
            {
                fragments.Add(item.Value);
            }

            fragments.AddRange(groupBy);
            foreach (PlanOrderItem term in orderBy)
            {
                if (term.Value is not null)
                {
                    fragments.Add(term.Value);
                }
            }

            if (where is not null)
            {
                fragments.Add(where);
            }

            if (having is not null)
            {
                fragments.Add(having);
            }

            ImmutableArray<JoinNode> joins = lowerer.Complete(
                fragments,
                new[] { skip, take }.Where(slot => slot is not null).Select(slot => slot!));
            return new RelationalPlan
            {
                Root = lowerer.Root,
                Joins = joins,
                Variables = [.. lowerer.Variables],
                Bindings = [.. lowerer.Bindings],
                Parameters = [.. lowerer.Slots],
                Select = select,
                Where = where,
                GroupBy = groupBy,
                Having = having,
                OrderBy = orderBy,
                Skip = skip,
                Take = take,
            };
        }

        /// <summary>The context every value-shaped clause is bound against.</summary>
        /// <returns>The context.</returns>
        private BindingContext ValueContext()
        {
            return Context(_spec.Aggregate ? QueryexMode.Aggregate : QueryexMode.Value);
        }

        /// <summary>The context for one mode.</summary>
        /// <param name="mode">The mode.</param>
        /// <returns>The context.</returns>
        private BindingContext Context(QueryexMode mode)
        {
            return new BindingContext
            {
                Schema = _options.Schema,
                Root = _spec.Root,
                Mode = mode,
                HasUser = _options.HasUser,
                HasGroupingKeys = _hasGroupingKeys,
                Parameters = _symbols,
            };
        }

        /// <summary>Binds the row-level predicate.</summary>
        private void BindFilter()
        {
            if (_spec.Filter is null)
            {
                return;
            }

            // Row-level whatever the query does: in a grouped query this decides which rows enter
            // the groups, which is why an access-control criterion belongs here and nowhere else.
            _filter = FilterBinder.Bind(
                _compiler,
                _spec.Filter,
                Context(QueryexMode.Filter),
                _options.Limits,
                _sink,
                DiagnosticLocation.Clause(QueryexClause.Filter),
                _budget,
                _nullity);
        }

        /// <summary>Binds the select list.</summary>
        /// <param name="syntax">The already-parsed list.</param>
        /// <param name="location">Which input it is.</param>
        private void BindSelect(ExpressionListSyntax syntax, DiagnosticLocation location)
        {
            _select = _compiler.Bind(
                _spec.Select,
                syntax,
                ValueContext(),
                directions: false,
                _options.Limits,
                _sink,
                location);
            _nullity.Absorb(_select.Nullity);
            _budget.TryConsumeTypedNodes(_select.NodeCount, default, _sink.Scope(location));
        }

        /// <summary>Binds the group-level predicate.</summary>
        private void BindHaving()
        {
            if (_spec.Having is null)
            {
                return;
            }

            _having = FilterBinder.Bind(
                _compiler,
                _spec.Having,
                Context(QueryexMode.AggregateFilter),
                _options.Limits,
                _sink,
                DiagnosticLocation.Clause(QueryexClause.Having),
                _budget,
                _nullity);
        }

        /// <summary>Binds the ordering list.</summary>
        private void BindOrderBy()
        {
            if (_spec.OrderBy is null)
            {
                return;
            }

            var location = DiagnosticLocation.Clause(QueryexClause.OrderBy);
            _compiler.TryCompile(
                _spec.OrderBy,
                ValueContext(),
                directions: true,
                _options.Limits,
                _sink,
                location,
                out _,
                out BoundExpression? bound);

            if (bound is null)
            {
                return;
            }

            _orderBy = bound;
            _nullity.Absorb(bound.Nullity);
            _budget.TryConsumeTypedNodes(bound.NodeCount, default, _sink.Scope(location));
        }

        /// <summary>Works out which select items become grouping keys.</summary>
        /// <remarks>
        ///     From the select list alone. If the ordering could contribute a key, changing how a
        ///     report is sorted would quietly change how many rows it returns.
        /// </remarks>
        private void DeriveKeys()
        {
            if (!_spec.Aggregate)
            {
                return;
            }

            for (int index = 0; index < _select.Items.Length; index++)
            {
                TypedExpr item = _select.Items[index].Expression;
                if (!item.ContainsAggregate && item.ContainsPath)
                {
                    _keys.Add(index);
                }
            }
        }

        /// <summary>Checks the rules an ordering list has to satisfy.</summary>
        private void CheckOrdering()
        {
            DiagnosticScope scope = _sink.Scope(DiagnosticLocation.Clause(QueryexClause.OrderBy));
            for (int index = 0; index < _orderBy.Items.Length; index++)
            {
                BoundItem item = _orderBy.Items[index];
                for (int earlier = 0; earlier < index; earlier++)
                {
                    if (TypedEquivalence.AreEquivalent(
                        item.Expression,
                        _orderBy.Items[earlier].Expression))
                    {
                        // The backend refuses the same column twice in one ordering, and a second
                        // mention could not change the order anyway, so it is a typo either way.
                        scope.Report(DiagnosticCodes.DuplicateOrderingTerm, item.Span);
                        break;
                    }
                }

                if (_spec.Aggregate && !OrdersWithinGrouping(item.Expression))
                {
                    scope.Report(DiagnosticCodes.OrderingAltersGrouping, item.Span);
                }
            }
        }

        /// <summary>Whether an ordering term leaves the grouping alone.</summary>
        /// <param name="term">The bound term.</param>
        /// <returns>True when it does.</returns>
        private bool OrdersWithinGrouping(TypedExpr term)
        {
            // A measure orders groups by what they measure, and a term that reads no column is the
            // same for every group; neither can widen what the query grouped by.
            if (term.ContainsAggregate || !term.ContainsPath)
            {
                return true;
            }

            foreach (int key in _keys)
            {
                if (TypedEquivalence.AreEquivalent(term, _select.Items[key].Expression))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Checks that a paged query says what order it is paging through.</summary>
        private void CheckPaging()
        {
            if (_spec.Skip is null && _spec.Take is null)
            {
                return;
            }

            if (_orderBy.Items.Length > 0)
            {
                return;
            }

            // Without an ordering the backend is free to return the rows in any order it likes, so
            // two requests for the same page can return different rows and a request for every page
            // can miss some entirely.
            _sink.Scope(DiagnosticLocation.Clause(QueryexClause.OrderBy))
                .Report(DiagnosticCodes.PagingRequiresOrdering, default);
        }

        /// <summary>Lowers the row-level predicate.</summary>
        /// <param name="lowerer">The lowering.</param>
        /// <returns>The lowered predicate, or null when there is none.</returns>
        private PlanPredicate? LowerFilter(Lowerer lowerer)
        {
            if (_filter is null)
            {
                return null;
            }

            lowerer.Begin(_nullity, DiagnosticLocation.Clause(QueryexClause.Filter), bindingsAllowed: true);
            return lowerer.LowerPredicate(_filter);
        }

        /// <summary>Lowers the select list, and the grouping keys along with it.</summary>
        /// <param name="lowerer">The lowering.</param>
        /// <returns>The lowered items and the grouping keys.</returns>
        private (ImmutableArray<PlanSelectItem> Select, ImmutableArray<PlanValue> GroupBy) LowerSelect(
            Lowerer lowerer)
        {
            var location = DiagnosticLocation.Clause(QueryexClause.Select);
            ImmutableArray<PlanSelectItem>.Builder items =
                ImmutableArray.CreateBuilder<PlanSelectItem>(_select.Items.Length);

            ImmutableArray<PlanValue>.Builder keys = ImmutableArray.CreateBuilder<PlanValue>(_keys.Count);

            for (int index = 0; index < _select.Items.Length; index++)
            {
                BoundItem item = _select.Items[index];
                bool isKey = _keys.Contains(index);

                // A grouping key is written into the grouping clause as well as the select list, and
                // a lateral binding is legal in both; a measure is neither, so nothing inside one may
                // refer to a binding except from within the aggregation itself.
                // A key is lowered in the general form. The backend refuses a grouping expression
                // that reads no column, and an item that folded to a constant would be exactly that
                // — while still having to appear in the grouping, because the query is grouped and
                // the item is not an aggregation.
                lowerer.Begin(
                    _nullity,
                    location.Item(index),
                    bindingsAllowed: !_spec.Aggregate || isKey,
                    folding: !isKey);

                PlanValue value = lowerer.LowerValue(item.Expression);
                if (isKey)
                {
                    keys.Add(value);
                }

                items.Add(new PlanSelectItem(
                    value,
                    StatementEmitter.ColumnName(index),
                    Describe(index, item, isKey)));
            }

            return (items.ToImmutable(), keys.ToImmutable());
        }

        /// <summary>Lowers the group-level predicate.</summary>
        /// <param name="lowerer">The lowering.</param>
        /// <returns>The lowered predicate, or null when there is none.</returns>
        private PlanPredicate? LowerHaving(Lowerer lowerer)
        {
            if (_having is null)
            {
                return null;
            }

            // Whatever a group-level predicate reads is read per group, and a lateral binding is per
            // row; the backend rejects a reference to one from here unless the grouping saw it.
            lowerer.Begin(_nullity, DiagnosticLocation.Clause(QueryexClause.Having), bindingsAllowed: false);
            return lowerer.LowerPredicate(_having);
        }

        /// <summary>Lowers the ordering list and appends whatever a page needs to be reproducible.</summary>
        /// <param name="lowerer">The lowering.</param>
        /// <param name="select">The lowered select list.</param>
        /// <returns>The ordering terms.</returns>
        private ImmutableArray<PlanOrderItem> LowerOrdering(
            Lowerer lowerer,
            ImmutableArray<PlanSelectItem> select)
        {
            var location = DiagnosticLocation.Clause(QueryexClause.OrderBy);
            ImmutableArray<PlanOrderItem>.Builder terms =
                ImmutableArray.CreateBuilder<PlanOrderItem>(_orderBy.Items.Length + 1);

            for (int index = 0; index < _orderBy.Items.Length; index++)
            {
                BoundItem item = _orderBy.Items[index];
                bool descending = item.Direction == QueryexDirection.Descending;

                int matching = MatchingSelectItem(item.Expression);
                if (matching >= 0)
                {
                    // Ordering by the item's name rather than by a second copy of the expression.
                    // Lowering it again would build a second value binding, and a grouped statement
                    // has to order by the very expression it grouped by.
                    terms.Add(new PlanOrderItem(null, select[matching].Alias, descending));
                    continue;
                }

                lowerer.Begin(_nullity, location.Item(index), bindingsAllowed: !_spec.Aggregate);
                PlanValue term = lowerer.LowerValue(item.Expression);
                if (!PlanWalk.ReadsAColumn(term) && !PlanWalk.Aggregates(term))
                {
                    // The same for every row, so it orders nothing. Dropped rather than written,
                    // because the backend refuses an ordering term that is a bare parameter and
                    // refuses a constant one outright — and either way the reader asked for an
                    // order this term was never going to give them.
                    continue;
                }

                terms.Add(new PlanOrderItem(term, null, descending));
            }

            AppendTiebreakers(select, terms);

            // CheckPaging proved an ordering was written, but the loop above drops every term that
            // orders nothing and a grand total has no keys for AppendTiebreakers to fall back on —
            // so the ordering can still be empty here, and OFFSET is grammatically part of ORDER BY.
            if ((_spec.Skip is not null || _spec.Take is not null) && terms.Count == 0)
            {
                _sink.Scope(location).Report(DiagnosticCodes.PagingRequiresOrdering, default);
            }

            return terms.ToImmutable();
        }

        /// <summary>Appends what a paged query needs to return the same rows twice running.</summary>
        /// <param name="select">The lowered select list.</param>
        /// <param name="terms">The ordering terms so far.</param>
        /// <remarks>
        ///     A reader's ordering rarely settles every tie, and paging through a partial order
        ///     returns overlapping pages and skips rows. What is appended is whatever makes the
        ///     order total: the key of the root row, or, over groups, every key the grouping used.
        /// </remarks>
        private void AppendTiebreakers(
            ImmutableArray<PlanSelectItem> select,
            ImmutableArray<PlanOrderItem>.Builder terms)
        {
            if (_spec.Skip is null && _spec.Take is null)
            {
                return;
            }

            if (_spec.Aggregate)
            {
                for (int index = 0; index < _keys.Count; index++)
                {
                    string alias = select[_keys[index]].Alias;
                    if (!terms.Any(term => term.Alias == alias))
                    {
                        terms.Add(new PlanOrderItem(null, alias, false));
                    }
                }

                return;
            }

            TypedPath key = new(default, [], _spec.Root.Key, [_spec.Root.Key.Name]);
            foreach (BoundItem item in _orderBy.Items)
            {
                if (TypedEquivalence.AreEquivalent(item.Expression, key))
                {
                    // Already ordered by; appending it would change nothing, and the backend refuses
                    // the same column twice in one ordering anyway.
                    return;
                }
            }

            int matching = MatchingSelectItem(key);
            if (matching >= 0)
            {
                terms.Add(new PlanOrderItem(null, select[matching].Alias, false));
                return;
            }

            terms.Add(new PlanOrderItem(
                new PlanColumn(default, _root!, _spec.Root.Key),
                null,
                false));
        }

        /// <summary>Which select item an expression matches, or -1 when none does.</summary>
        /// <param name="expression">The expression.</param>
        /// <returns>The item's position, or -1.</returns>
        private int MatchingSelectItem(TypedExpr expression)
        {
            for (int index = 0; index < _select.Items.Length; index++)
            {
                if (TypedEquivalence.AreEquivalent(expression, _select.Items[index].Expression))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>Describes one select item to the host.</summary>
        /// <param name="ordinal">The item's position.</param>
        /// <param name="item">The bound item.</param>
        /// <param name="isKey">Whether it becomes a grouping key.</param>
        /// <returns>The column description.</returns>
        private QueryexColumn Describe(int ordinal, BoundItem item, bool isKey)
        {
            IReadOnlyList<string>? path = item.Expression is TypedPath bare ? bare.Segments : null;
            return new QueryexColumn(
                ordinal,
                _spec.Select.Substring(item.Span.Start, item.Span.Length),
                BoundTypes.ToPublic(item.Expression.Type),
                _nullity[item.Expression],
                path,
                isKey);
        }
    }
}
