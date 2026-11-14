// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Lowering;

namespace Tellma.Core.Queryex.Emit
{
    /// <summary>
    ///     Lays a whole lowered query out as one batch.
    /// </summary>
    /// <remarks>
    ///     The order of everything here is fixed by the plan, not chosen while writing: joins come
    ///     out in the order their aliases were assigned, bindings in the order they were made, and
    ///     items in the order they were written. That is what makes the same query produce the same
    ///     bytes every time, which is in turn what makes a cached plan and a stored snapshot worth
    ///     anything.
    /// </remarks>
    internal static class StatementEmitter
    {
        /// <summary>The prefix every result column's name shares.</summary>
        internal const string ColumnPrefix = "c";

        /// <summary>The text that ends a statement written on its own.</summary>
        internal const string Terminator = ";\n";

        /// <summary>
        ///     The clause that has the backend plan the statement afresh at each execution, against
        ///     the values its declarations hold then.
        /// </summary>
        internal const string RecompileHint = "OPTION (RECOMPILE)\n";

        /// <summary>Assembles the batch a statement is executed as on its own.</summary>
        /// <param name="prologue">The declarations the statement depends on.</param>
        /// <param name="body">The statement, without its terminator.</param>
        /// <param name="recompile">Whether the statement is planned afresh at each execution.</param>
        /// <returns>
        ///     The declarations, the statement, the hint when there is one, and the terminator.
        /// </returns>
        /// <remarks>
        ///     The hint is the statement's own clause and so follows the body rather than sitting in
        ///     it: a body is embedded as a derived table or behind a condition, where the backend
        ///     takes no hint, and a host that embeds it writes the one clause its own statement may
        ///     carry.
        /// </remarks>
        internal static string Batch(string prologue, string body, bool recompile)
        {
            return prologue + body + (recompile ? RecompileHint : string.Empty) + Terminator;
        }

        /// <summary>Writes one lowered query as SQL.</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>
        ///     The declarations the statement depends on, and the statement without its terminator.
        ///     <see cref="Batch" /> assembles the whole batch from the two.
        /// </returns>
        /// <remarks>
        ///     Written as two parts rather than cut out of one, so a host embedding the statement in
        ///     its own SQL never has to find where the declarations end or strip a terminator — both
        ///     of which are parsing compiled text, which nothing outside the engine may do.
        /// </remarks>
        internal static (string Prologue, string Body) Write(RelationalPlan plan)
        {
            // The hoisted declarations, then one empty line, or nothing at all.
            SqlWriter prologue = new();
            PlanEmitter declarations = new(prologue);
            foreach (HoistedVariable variable in plan.Variables)
            {
                declarations.Declaration(variable);
            }

            if (plan.Variables.Length > 0)
            {
                prologue.Line();
            }

            // The statement, clause by clause, each ending its own line.
            SqlWriter body = new();
            PlanEmitter emitter = new(body);
            WriteSelect(body, emitter, plan);
            WriteFrom(body, emitter, plan);
            WriteWhere(body, emitter, plan);
            WriteGrouping(body, emitter, plan);
            WriteOrdering(body, emitter, plan);
            WritePaging(body, plan);

            return (prologue.ToString(), body.ToString());
        }

        /// <summary>Writes the select list.</summary>
        /// <param name="sql">Where the SQL is collected.</param>
        /// <param name="emitter">The expression writer.</param>
        /// <param name="plan">The plan.</param>
        private static void WriteSelect(SqlWriter sql, PlanEmitter emitter, RelationalPlan plan)
        {
            sql.Write("SELECT");
            sql.Line();
            for (int index = 0; index < plan.Select.Length; index++)
            {
                PlanSelectItem item = plan.Select[index];
                sql.Write("    ");
                emitter.Value(item.Value);
                sql.Write(" AS ");
                sql.Quoted(item.Alias);
                if (index < plan.Select.Length - 1)
                {
                    sql.Write(',');
                }

                sql.Line();
            }
        }

        /// <summary>Writes the source, its joins, and its one-row sources.</summary>
        /// <param name="sql">Where the SQL is collected.</param>
        /// <param name="emitter">The expression writer.</param>
        /// <param name="plan">The plan.</param>
        private static void WriteFrom(SqlWriter sql, PlanEmitter emitter, RelationalPlan plan)
        {
            if (plan.Root is not JoinNode root)
            {
                // No row source, so no clause at all: the backend answers a select list on its own
                // with one row. Nothing can join or apply here, since both read a row.
                return;
            }

            sql.Write("FROM ");
            sql.Write(root.Entity.Source);
            sql.Write(" AS ");
            sql.Quoted(root.Alias);
            sql.Line();

            // One join or one-row source per line; a subquery writes the same parts on one line of
            // its own.
            foreach (JoinNode join in plan.Joins)
            {
                emitter.Join(join);
                sql.Line();
            }

            foreach (LateralSource lateral in plan.Laterals)
            {
                emitter.Lateral(lateral);
                sql.Line();
            }
        }

        /// <summary>Writes the row-level predicate.</summary>
        /// <param name="sql">Where the SQL is collected.</param>
        /// <param name="emitter">The expression writer.</param>
        /// <param name="plan">The plan.</param>
        private static void WriteWhere(SqlWriter sql, PlanEmitter emitter, RelationalPlan plan)
        {
            if (plan.Where is null)
            {
                return;
            }

            sql.Write("WHERE ");
            emitter.Predicate(plan.Where);
            sql.Line();
        }

        /// <summary>Writes the grouping keys and the group-level predicate.</summary>
        /// <param name="sql">Where the SQL is collected.</param>
        /// <param name="emitter">The expression writer.</param>
        /// <param name="plan">The plan.</param>
        private static void WriteGrouping(SqlWriter sql, PlanEmitter emitter, RelationalPlan plan)
        {
            if (plan.GroupBy.Length > 0)
            {
                sql.Write("GROUP BY ");
                for (int index = 0; index < plan.GroupBy.Length; index++)
                {
                    if (index > 0)
                    {
                        sql.Write(", ");
                    }

                    emitter.Value(plan.GroupBy[index]);
                }

                sql.Line();
            }

            if (plan.Having is null)
            {
                return;
            }

            sql.Write("HAVING ");
            emitter.Predicate(plan.Having);
            sql.Line();
        }

        /// <summary>Writes the ordering terms.</summary>
        /// <param name="sql">Where the SQL is collected.</param>
        /// <param name="emitter">The expression writer.</param>
        /// <param name="plan">The plan.</param>
        private static void WriteOrdering(SqlWriter sql, PlanEmitter emitter, RelationalPlan plan)
        {
            if (plan.OrderBy.Length == 0)
            {
                return;
            }

            sql.Write("ORDER BY ");
            for (int index = 0; index < plan.OrderBy.Length; index++)
            {
                if (index > 0)
                {
                    sql.Write(", ");
                }

                PlanOrderItem item = plan.OrderBy[index];
                if (item.Alias is not null)
                {
                    sql.Quoted(item.Alias);
                }
                else if (item.Value is not null)
                {
                    emitter.Value(item.Value);
                }

                // Written out even though one of the two is the backend's default, because the sort
                // is part of what a page means and leaving it implicit would make that depend on a
                // default rather than on the query.
                sql.Write(item.Descending ? " DESC" : " ASC");
            }

            sql.Line();
        }

        /// <summary>Writes the paging clause.</summary>
        /// <param name="sql">Where the SQL is collected.</param>
        /// <param name="plan">The plan.</param>
        private static void WritePaging(SqlWriter sql, RelationalPlan plan)
        {
            if (plan.Skip is null && plan.Take is null)
            {
                return;
            }

            sql.Write("OFFSET ");
            sql.Write(plan.Skip?.Name ?? "0");
            sql.Write(" ROWS");

            if (plan.Take is ParameterSlot take)
            {
                sql.Write(" FETCH NEXT ");
                sql.Write(take.Name);
                sql.Write(" ROWS ONLY");
            }

            sql.Line();
        }

        /// <summary>The name the item at a given position is written under.</summary>
        /// <param name="ordinal">The zero-based position.</param>
        /// <returns>The name.</returns>
        internal static string ColumnName(int ordinal)
        {
            return ColumnPrefix + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Turns the plan's slots into what the host binds.</summary>
        /// <param name="slots">The slots.</param>
        /// <returns>The public parameter list.</returns>
        internal static ImmutableArray<QueryexParameterSlot> Parameters(ImmutableArray<ParameterSlot> slots)
        {
            ImmutableArray<QueryexParameterSlot>.Builder builder =
                ImmutableArray.CreateBuilder<QueryexParameterSlot>(slots.Length);

            foreach (ParameterSlot slot in slots)
            {
                builder.Add(new QueryexParameterSlot(
                    slot.Name,
                    Binding.BoundTypes.ToPublic(slot.Type),
                    slot.StoreType,
                    slot.Origin,
                    slot.Value,
                    slot.DeclaredName));
            }

            return builder.ToImmutable();
        }
    }
}
