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

        /// <summary>Writes one lowered query as SQL.</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The batch.</returns>
        internal static string Write(RelationalPlan plan)
        {
            SqlWriter sql = new();
            PlanEmitter emitter = new(sql);

            foreach (HoistedVariable variable in plan.Variables)
            {
                emitter.Declaration(variable);
            }

            if (plan.Variables.Length > 0)
            {
                sql.Line();
            }

            WriteSelect(sql, emitter, plan);
            WriteFrom(sql, emitter, plan);
            WriteWhere(sql, emitter, plan);
            WriteGrouping(sql, emitter, plan);
            WriteOrdering(sql, emitter, plan);
            WritePaging(sql, plan);

            sql.Write(';');
            sql.Line();
            return sql.ToString();
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

        /// <summary>Writes the source, its joins, and its bindings.</summary>
        /// <param name="sql">Where the SQL is collected.</param>
        /// <param name="emitter">The expression writer.</param>
        /// <param name="plan">The plan.</param>
        private static void WriteFrom(SqlWriter sql, PlanEmitter emitter, RelationalPlan plan)
        {
            sql.Write("FROM ");
            sql.Write(plan.Root.Entity.Source);
            sql.Write(" AS ");
            sql.Quoted(plan.Root.Alias);
            sql.Line();

            foreach (JoinNode join in plan.Joins)
            {
                sql.Write(join.IsInner ? "INNER JOIN " : "LEFT JOIN ");
                sql.Write(join.Entity.Source);
                sql.Write(" AS ");
                sql.Quoted(join.Alias);
                sql.Write(" ON ");
                sql.Quoted(join.Alias);
                sql.Write('.');
                sql.Quoted(join.Entity.Key.Column);
                sql.Write(" = ");
                sql.Quoted(join.Parent!.Alias);
                sql.Write('.');
                sql.Quoted(join.Navigation!.ForeignKey.Column);
                sql.Line();
            }

            foreach (ValueBinding binding in plan.Bindings)
            {
                // A one-row lateral source: it names a value the guards can mention as often as they
                // need to without the expression behind it being written more than once.
                sql.Write("CROSS APPLY (VALUES (");
                emitter.Value(binding.Value);
                sql.Write(")) AS ");
                sql.Quoted(binding.Alias);
                sql.Write(" (");
                sql.Quoted(PlanEmitter.BindingColumn);
                sql.Write(')');
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
