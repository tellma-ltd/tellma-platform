// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Nullity;
using Tellma.Core.Queryex.Pipeline;
using Tellma.Core.Queryex.Syntax;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>
    ///     A whole query, answered from memory.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The other half of the differential check: the compiler turns a query into SQL a server
    ///         answers, and this answers the same query from the same rows by following the rules
    ///         directly. Where the two answers differ, one of them is wrong.
    ///     </para>
    ///     <para>
    ///         Parameters the context leaves out are still bound here, every one of them: what
    ///         leaving one out means is applied while the query is answered, never by rewriting it
    ///         first, which is the compiler's way and so the one this must not share.
    ///     </para>
    /// </remarks>
    public static class ReferenceQuery
    {
        /// <summary>The stages, shared because this reading only ever asks them to bind.</summary>
        private static readonly ExpressionCompiler Compiler = new(new QueryexEngineOptions());

        /// <summary>Answers one query from the rows in memory.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="declarations">The declared parameters.</param>
        /// <returns>The rows, each a value per select item.</returns>
        public static IReadOnlyList<IReadOnlyList<QxValue>> Run(
            QuerySpec spec,
            InterpreterContext context,
            IReadOnlyList<QueryexParameterDeclaration>? declarations = null)
        {
            ArgumentNullException.ThrowIfNull(spec);

            return Run(spec, context, LedgerFixture.Schema, LedgerData.Rows(spec.Root), declarations);
        }

        /// <summary>Answers one query from rows the caller supplies, bound against a schema of its own.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="schema">The schema the query is bound against.</param>
        /// <param name="rows">The root entity's rows.</param>
        /// <param name="declarations">The declared parameters.</param>
        /// <returns>The rows, each a value per select item.</returns>
        /// <remarks>
        ///     For a table too large to write out as statements, which a suite generates on the server
        ///     and here alike. Only the root's rows are supplied, so the query follows no navigation.
        /// </remarks>
        public static IReadOnlyList<IReadOnlyList<QxValue>> Run(
            QuerySpec spec,
            InterpreterContext context,
            QueryexSchema schema,
            IReadOnlyList<LedgerRow> rows,
            IReadOnlyList<QueryexParameterDeclaration>? declarations = null)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(rows);

            Plan plan = Prepare(spec, context, schema, declarations ?? []);
            rows = Filtered(spec, plan, context, rows);

            List<Answer> answers = spec.Aggregate
                ? Grouped(plan, context, rows)
                : [.. rows.Select(row => Project(plan, context, [row], row))];

            Sort(spec, plan, context, answers);
            return [.. Paged(spec, answers).Select(answer => answer.Values)];
        }

        /// <summary>One answered row, with what it needs to be ordered by.</summary>
        /// <param name="Values">The value of each select item.</param>
        /// <param name="Rows">The rows it was answered from.</param>
        /// <param name="Anchor">The row that supplies the tiebreaker, when there is one.</param>
        private sealed record Answer(IReadOnlyList<QxValue> Values, IReadOnlyList<LedgerRow> Rows, LedgerRow? Anchor);

        /// <summary>The bound clauses of one query.</summary>
        /// <param name="Select">The select list.</param>
        /// <param name="Filter">The row-level predicate, when there is one.</param>
        /// <param name="Having">The group-level predicate, when there is one.</param>
        /// <param name="OrderBy">The ordering list.</param>
        /// <param name="Keys">Which select items became grouping keys.</param>
        /// <param name="Undeclared">The columns read after the select list that the schema leaves out.</param>
        private sealed record Plan(
            ImmutableArray<BoundItem> Select,
            TypedExpr? Filter,
            TypedExpr? Having,
            ImmutableArray<BoundItem> OrderBy,
            IReadOnlyList<int> Keys,
            IReadOnlyList<string> Undeclared);

        /// <summary>Binds every clause of a query.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="schema">The schema the query is bound against.</param>
        /// <param name="declarations">The declared parameters.</param>
        /// <returns>The bound clauses.</returns>
        private static Plan Prepare(
            QuerySpec spec,
            InterpreterContext context,
            QueryexSchema schema,
            IReadOnlyList<QueryexParameterDeclaration> declarations)
        {
            DiagnosticSink sink = new();
            CompilationBudget budget = new(QueryexLimits.Default);
            NullityMap nullity = new();

            Compiler.TryParse(
                spec.Select,
                QueryexLimits.Default,
                sink,
                DiagnosticLocation.Clause(QueryexClause.Select),
                out ExpressionListSyntax? syntax);

            bool grouped = spec.Aggregate && QueryCompiler.DerivesGroupingKeys(syntax!);
            BindingContext value = Context(schema, spec, context, declarations, grouped, spec.Aggregate
                ? QueryexMode.Aggregate
                : QueryexMode.Value);

            BoundExpression select = Compiler.Bind(
                spec.Select,
                syntax!,
                value,
                directions: false,
                QueryexLimits.Default,
                sink,
                DiagnosticLocation.Clause(QueryexClause.Select));

            TypedExpr? filter = spec.Filter is null
                ? null
                : FilterBinder.Bind(
                    Compiler,
                    spec.Filter,
                    Context(schema, spec, context, declarations, grouped, QueryexMode.Filter),
                    QueryexLimits.Default,
                    sink,
                    DiagnosticLocation.Clause(QueryexClause.Filter),
                    budget,
                    nullity);

            TypedExpr? having = spec.Having is null
                ? null
                : FilterBinder.Bind(
                    Compiler,
                    spec.Having,
                    Context(schema, spec, context, declarations, grouped, QueryexMode.AggregateFilter),
                    QueryexLimits.Default,
                    sink,
                    DiagnosticLocation.Clause(QueryexClause.Having),
                    budget,
                    nullity);

            ImmutableArray<BoundItem> ordering = [];
            if (spec.OrderBy is not null)
            {
                Compiler.TryCompile(
                    spec.OrderBy,
                    value,
                    directions: true,
                    QueryexLimits.Default,
                    sink,
                    DiagnosticLocation.Clause(QueryexClause.OrderBy),
                    out _,
                    out BoundExpression? bound);

                ordering = bound?.Items ?? [];
            }

            List<int> keys = [];
            if (spec.Aggregate)
            {
                for (int index = 0; index < select.Items.Length; index++)
                {
                    TypedExpr item = select.Items[index].Expression;
                    if (!item.ContainsAggregate && item.ContainsPath)
                    {
                        keys.Add(index);
                    }
                }
            }

            return new Plan(select.Items, filter, having, ordering, keys, spec.UndeclaredColumns);
        }

        /// <summary>The binding context for one clause.</summary>
        /// <param name="schema">The schema the query is bound against.</param>
        /// <param name="spec">The query.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="declarations">The declared parameters.</param>
        /// <param name="grouped">Whether the select list derived any grouping key.</param>
        /// <param name="mode">The position the clause is written for.</param>
        /// <returns>The context.</returns>
        private static BindingContext Context(
            QueryexSchema schema,
            QuerySpec spec,
            InterpreterContext context,
            IReadOnlyList<QueryexParameterDeclaration> declarations,
            bool grouped,
            QueryexMode mode)
        {
            return new BindingContext
            {
                Schema = schema,
                Root = spec.Root,
                Mode = mode,
                HasUser = context.UserId is not null,
                HasGroupingKeys = grouped,
                Parameters = Symbols.From(declarations),
            };
        }

        /// <summary>The rows the row-level predicate keeps.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="plan">The bound clauses.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="rows">The root entity's rows.</param>
        /// <returns>The rows.</returns>
        private static IReadOnlyList<LedgerRow> Filtered(
            QuerySpec spec,
            Plan plan,
            InterpreterContext context,
            IReadOnlyList<LedgerRow> rows)
        {
            if (plan.Filter is null && spec.Restrictions.Count == 0)
            {
                return rows;
            }

            // A row is kept when the filter holds and every restriction's key is in its table: the
            // restrictions are further conjuncts, read here straight from the rows rather than
            // through anything the compiler built.
            List<LedgerRow> kept = [];
            foreach (LedgerRow row in rows)
            {
                bool passes = plan.Filter is null || new Interpreter(row, context).Decides(plan.Filter);
                if (passes && spec.Restrictions.All(restriction => Holds(restriction, row, context)))
                {
                    kept.Add(row);
                }
            }

            return kept;
        }

        /// <summary>Whether a row's key is in the table a restriction names.</summary>
        /// <param name="restriction">The restriction.</param>
        /// <param name="row">The row.</param>
        /// <param name="context">What the evaluation depends on, the tables of keys included.</param>
        /// <returns>True when the row's key is present and held by the table.</returns>
        /// <exception cref="InvalidOperationException">The case supplied no table by that name.</exception>
        private static bool Holds(KeySetRestriction restriction, LedgerRow row, InterpreterContext context)
        {
            KeySet keys = context.KeySets.GetValueOrDefault(restriction.TableSource)
                ?? throw new InvalidOperationException(
                    "No key set named " + restriction.TableSource + " was supplied.");

            return keys.Holds(KeyOf(row, restriction.Path));
        }

        /// <summary>Reads a restriction's key from a row, following each navigation by name.</summary>
        /// <param name="row">The row.</param>
        /// <param name="path">The restriction's dotted path.</param>
        /// <returns>The key, or absence when a step along the way points at nothing.</returns>
        private static QxValue KeyOf(LedgerRow row, string path)
        {
            string[] segments = path.Split('.');
            LedgerRow? current = row;
            foreach (string segment in segments[..^1])
            {
                NavigationDescriptor navigation = current.Entity.Navigations.Single(
                    candidate => string.Equals(candidate.Name, segment, StringComparison.OrdinalIgnoreCase));

                current = LedgerData.Find(navigation.Target, current[navigation.ForeignKey.Name]);
                if (current is null)
                {
                    return QxValue.Absent(QueryexType.QxNumeric);
                }
            }

            return current[segments[^1]];
        }

        /// <summary>Answers a grouped query.</summary>
        /// <param name="plan">The bound clauses.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="rows">The rows that survived the row-level predicate.</param>
        /// <returns>One answer per group.</returns>
        private static List<Answer> Grouped(
            Plan plan,
            InterpreterContext context,
            IReadOnlyList<LedgerRow> rows)
        {
            List<Answer> answers = [];
            if (plan.Keys.Count == 0)
            {
                // No keys at all is a grand total: one answer over every row, and one even when
                // there are no rows to total.
                Answer total = Project(plan, context, rows, null);
                if (Keeps(plan, context, rows, total))
                {
                    answers.Add(total);
                }

                return answers;
            }

            List<(List<QxValue> Key, List<LedgerRow> Rows)> groups = [];
            foreach (LedgerRow row in rows)
            {
                List<QxValue> key = [];
                Interpreter reading = new(row, context);
                foreach (int index in plan.Keys)
                {
                    key.Add(reading.Evaluate(plan.Select[index].Expression));
                }

                (List<QxValue> _, List<LedgerRow> members) = groups.Find(group => Same(group.Key, key));
                if (members is null)
                {
                    groups.Add((key, [row]));
                }
                else
                {
                    members.Add(row);
                }
            }

            foreach ((List<QxValue> _, List<LedgerRow> members) in groups)
            {
                Answer answer = Project(plan, context, members, members[0]);
                if (Keeps(plan, context, members, answer))
                {
                    answers.Add(answer);
                }
            }

            return answers;
        }

        /// <summary>Whether the group-level predicate keeps a group.</summary>
        /// <param name="plan">The bound clauses.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="rows">The group's rows.</param>
        /// <param name="answer">The answer for the group.</param>
        /// <returns>True when it keeps it.</returns>
        private static bool Keeps(
            Plan plan,
            InterpreterContext context,
            IReadOnlyList<LedgerRow> rows,
            Answer answer)
        {
            if (plan.Having is null)
            {
                return true;
            }

            LedgerRow anchor = answer.Anchor ?? (rows.Count > 0 ? rows[0] : LedgerData.Rows(LedgerFixture.Invoice)[0]);
            Interpreter reading = new(anchor, context) { Group = rows };
            return reading.Decides(plan.Having);
        }

        /// <summary>Whether two grouping keys are the same key.</summary>
        /// <param name="left">One key.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they are.</returns>
        /// <remarks>
        ///     Two missing values group together, which is what grouping does and what the
        ///     language's equality already says.
        /// </remarks>
        private static bool Same(List<QxValue> left, List<QxValue> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                bool equal = left[index].IsAbsent || right[index].IsAbsent
                    ? left[index].IsAbsent && right[index].IsAbsent
                    : Interpreter.Order(left[index], right[index]) == 0;

                if (!equal)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Evaluates the select list for one answer.</summary>
        /// <param name="plan">The bound clauses.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="rows">The rows the answer is built from.</param>
        /// <param name="anchor">The row that supplies row-level values.</param>
        /// <returns>The answer.</returns>
        private static Answer Project(
            Plan plan,
            InterpreterContext context,
            IReadOnlyList<LedgerRow> rows,
            LedgerRow? anchor)
        {
            LedgerRow reading = anchor ?? (rows.Count > 0 ? rows[0] : LedgerData.Rows(LedgerFixture.Invoice)[0]);
            Interpreter interpreter = new(reading, context) { Group = rows };

            List<QxValue> values = [];
            foreach (BoundItem item in plan.Select)
            {
                values.Add(interpreter.Evaluate(item.Expression));
            }

            // Read as stored, after everything compiled: the schema says nothing about these.
            foreach (string column in plan.Undeclared)
            {
                values.Add(reading.Undeclared.GetValueOrDefault(column) ?? QxValue.Absent(QueryexType.QxString));
            }

            return new Answer(values, rows, anchor);
        }

        /// <summary>Puts the answers in the order the query asked for.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="plan">The bound clauses.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="answers">The answers.</param>
        private static void Sort(
            QuerySpec spec,
            Plan plan,
            InterpreterContext context,
            List<Answer> answers)
        {
            if (plan.OrderBy.Length == 0 && spec.Skip is null && spec.Take is null)
            {
                return;
            }

            answers.Sort((left, right) => Rank(spec, plan, context, left, right));
        }

        /// <summary>Which of two answers comes first.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="plan">The bound clauses.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="left">One answer.</param>
        /// <param name="right">The other.</param>
        /// <returns>Negative, zero, or positive.</returns>
        private static int Rank(
            QuerySpec spec,
            Plan plan,
            InterpreterContext context,
            Answer left,
            Answer right)
        {
            foreach (BoundItem term in plan.OrderBy)
            {
                QxValue one = Term(plan, context, left, term.Expression);
                QxValue other = Term(plan, context, right, term.Expression);
                int order = Placed(one, other, term.Direction == QueryexDirection.Descending);
                if (order != 0)
                {
                    return order;
                }
            }

            return Tiebreak(spec, plan, left, right);
        }

        /// <summary>Orders two values, putting missing ones where the direction puts them.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <param name="descending">Whether the ordering runs downward.</param>
        /// <returns>Negative, zero, or positive.</returns>
        /// <remarks>
        ///     Missing values come first ascending and last descending, which is what the backend
        ///     does and what the language fixes so that paging is reproducible.
        /// </remarks>
        private static int Placed(QxValue left, QxValue right, bool descending)
        {
            if (left.IsAbsent || right.IsAbsent)
            {
                return left.IsAbsent && right.IsAbsent ? 0 : left.IsAbsent == descending ? 1 : -1;
            }

            int order = Interpreter.Order(left, right);
            return descending ? -order : order;
        }

        /// <summary>What one ordering term evaluates to for one answer.</summary>
        /// <param name="plan">The bound clauses.</param>
        /// <param name="context">What the evaluation depends on besides the rows.</param>
        /// <param name="answer">The answer.</param>
        /// <param name="term">The term.</param>
        /// <returns>Its value.</returns>
        private static QxValue Term(Plan plan, InterpreterContext context, Answer answer, TypedExpr term)
        {
            for (int index = 0; index < plan.Select.Length; index++)
            {
                if (TypedEquivalence.AreEquivalent(term, plan.Select[index].Expression))
                {
                    return answer.Values[index];
                }
            }

            LedgerRow reading = answer.Anchor
                ?? (answer.Rows.Count > 0 ? answer.Rows[0] : LedgerData.Rows(LedgerFixture.Invoice)[0]);

            return new Interpreter(reading, context) { Group = answer.Rows }.Evaluate(term);
        }

        /// <summary>Settles a tie the way the appended ordering terms settle it.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="plan">The bound clauses.</param>
        /// <param name="left">One answer.</param>
        /// <param name="right">The other.</param>
        /// <returns>Negative, zero, or positive.</returns>
        private static int Tiebreak(QuerySpec spec, Plan plan, Answer left, Answer right)
        {
            if (spec.Aggregate)
            {
                foreach (int index in plan.Keys)
                {
                    int order = Placed(left.Values[index], right.Values[index], descending: false);
                    if (order != 0)
                    {
                        return order;
                    }
                }

                return 0;
            }

            QxValue one = left.Anchor?[spec.Root.Key.Name] ?? QxValue.Absent(QueryexType.QxNumeric);
            QxValue other = right.Anchor?[spec.Root.Key.Name] ?? QxValue.Absent(QueryexType.QxNumeric);
            return Placed(one, other, descending: false);
        }

        /// <summary>The page of answers the query asked for.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="answers">The ordered answers.</param>
        /// <returns>The page.</returns>
        private static IEnumerable<Answer> Paged(QuerySpec spec, List<Answer> answers)
        {
            IEnumerable<Answer> page = answers;
            if (spec.Skip is int skip)
            {
                page = page.Skip(skip);
            }

            if (spec.Take is int take)
            {
                page = page.Take(take);
            }

            return page;
        }
    }
}
