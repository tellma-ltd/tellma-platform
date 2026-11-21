// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;

namespace Tellma.Core.Queryex.Lowering
{
    /// <summary>
    ///     One node of the join tree: the root, or one navigation step reached from another node.
    /// </summary>
    /// <remarks>
    ///     Built from every distinct navigation prefix any clause mentions, so a navigation referred
    ///     to from two clauses becomes one join with one alias rather than two of each.
    /// </remarks>
    internal sealed class JoinNode
    {
        /// <summary>The steps out of this node, in the order the schema declares them.</summary>
        private readonly List<JoinNode> _children = [];

        /// <summary>Initializes a node.</summary>
        /// <param name="entity">The entity this node reads.</param>
        /// <param name="navigation">The step that reached it, or null at the root.</param>
        /// <param name="parent">The node it was reached from, or null at the root.</param>
        /// <param name="origin">Where the path that first reached it was written.</param>
        internal JoinNode(
            EntityDescriptor entity,
            NavigationDescriptor? navigation,
            JoinNode? parent,
            PlanOrigin origin)
        {
            Entity = entity;
            Navigation = navigation;
            Parent = parent;
            Origin = origin;

            // Inner only where the step cannot be missing and nothing above it can be either. A
            // step may be missing through a nullable foreign key or because the schema declares
            // it optional. Behind an optional step even a mandatory one may have no row, and an
            // inner join there would silently drop rows whose optional ancestor is absent.
            IsInner = parent is null
                || (navigation is not null && !navigation.MayBeMissing && parent.IsInner);
        }

        /// <summary>The entity this node reads.</summary>
        internal EntityDescriptor Entity { get; }

        /// <summary>The step that reached it, or null at the root.</summary>
        internal NavigationDescriptor? Navigation { get; }

        /// <summary>The node it was reached from, or null at the root.</summary>
        internal JoinNode? Parent { get; }

        /// <summary>Where the path that first reached it was written.</summary>
        internal PlanOrigin Origin { get; }

        /// <summary>Whether the join keeps only rows that have a match.</summary>
        internal bool IsInner { get; }

        /// <summary>The steps out of this node.</summary>
        internal IReadOnlyList<JoinNode> Children => _children;

        /// <summary>The alias this node is written under, assigned once the tree is settled.</summary>
        internal string Alias { get; set; } = string.Empty;

        /// <summary>Whether anything that survived lowering reads this node.</summary>
        internal bool IsReferenced { get; set; }

        /// <summary>
        ///     Where the first surviving reference that reads this node was written, once one has;
        ///     null for a root, which is read whatever survives.
        /// </summary>
        internal PlanOrigin? ReachedFrom { get; private set; }

        /// <summary>Finds or creates the step for a navigation.</summary>
        /// <param name="navigation">The navigation.</param>
        /// <param name="origin">Where the path taking the step was written.</param>
        /// <returns>The node it leads to.</returns>
        internal JoinNode Step(NavigationDescriptor navigation, PlanOrigin origin)
        {
            foreach (JoinNode child in _children)
            {
                if (ReferenceEquals(child.Navigation, navigation))
                {
                    return child;
                }
            }

            JoinNode created = new(navigation.Target, navigation, this, origin);
            _children.Add(created);
            return created;
        }

        /// <summary>Marks this node and everything above it as read.</summary>
        /// <param name="origin">Where the reference that reads it was written.</param>
        /// <remarks>
        ///     A node already read keeps the reference that read it first, and so does everything
        ///     above it; every node marked here is read on behalf of this reference.
        /// </remarks>
        internal void MarkReferenced(PlanOrigin origin)
        {
            JoinNode? current = this;
            while (current is not null && !current.IsReferenced)
            {
                current.IsReferenced = true;
                current.ReachedFrom = origin;
                current = current.Parent;
            }
        }

        /// <summary>Walks this node and its descendants, parents first.</summary>
        /// <returns>The nodes in order.</returns>
        /// <remarks>
        ///     Children are visited in the order the schema declares their navigations rather than
        ///     in the order a clause happened to mention them, so editing a filter cannot renumber
        ///     the aliases of joins it has nothing to do with.
        /// </remarks>
        internal IEnumerable<JoinNode> Walk()
        {
            yield return this;

            foreach (JoinNode child in _children.OrderBy(child => DeclarationOrderOf(child.Navigation!)))
            {
                foreach (JoinNode descendant in child.Walk())
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>Where a navigation sits among this node's entity's declarations.</summary>
        /// <param name="navigation">The navigation.</param>
        /// <returns>Its position.</returns>
        private int DeclarationOrderOf(NavigationDescriptor navigation)
        {
            IReadOnlyList<NavigationDescriptor> declared = Entity.Navigations;
            for (int index = 0; index < declared.Count; index++)
            {
                if (ReferenceEquals(declared[index], navigation))
                {
                    return index;
                }
            }

            return declared.Count;
        }
    }

    /// <summary>Where in the input something the plan holds was written.</summary>
    /// <param name="Span">The range.</param>
    /// <param name="Location">Which clause, and which item of it.</param>
    /// <remarks>
    ///     Kept so that a ceiling counted once the plan is settled can still name what pushed the
    ///     count over, rather than blaming the query as a whole.
    /// </remarks>
    internal readonly record struct PlanOrigin(QueryexSpan Span, DiagnosticLocation Location);

    /// <summary>One parameter of the statement being built.</summary>
    /// <param name="Type">The value's type in the language.</param>
    /// <param name="StoreType">The type to bind it as.</param>
    /// <param name="Origin">Where the value comes from.</param>
    /// <param name="Value">The value, when it was fixed at compile time.</param>
    /// <param name="DeclaredName">The declared parameter's name, when it has one.</param>
    /// <param name="Written">Where the value was written.</param>
    internal sealed record ParameterSlot(
        BoundType Type,
        QueryexStoreType StoreType,
        QueryexParameterOrigin Origin,
        object? Value,
        string? DeclaredName,
        PlanOrigin Written)
    {
        /// <summary>The name this slot is written under.</summary>
        internal string Name { get; set; } = string.Empty;
    }

    /// <summary>
    ///     A one-row source applied to each row of a join tree: a value binding or a calendar
    ///     lookup.
    /// </summary>
    /// <remarks>
    ///     The two kinds are written in one sequence, in the order they were made. A lookup may read
    ///     a binding made before it, and a later lookup may read an earlier one's month, so the order
    ///     of creation is an order their dependencies already respect.
    /// </remarks>
    internal abstract class LateralSource
    {
        /// <summary>Initializes the source.</summary>
        private protected LateralSource()
        {
        }

        /// <summary>The alias this source is written under, assigned once the plan is settled.</summary>
        internal string Alias { get; set; } = string.Empty;

        /// <summary>The values the source reads, which keep whatever they read alive.</summary>
        internal abstract ImmutableArray<PlanNode> Reads { get; }
    }

    /// <summary>
    ///     A value computed once per row and given a name, so the guards that need it more than once
    ///     do not have to recompute it.
    /// </summary>
    internal sealed class ValueBinding : LateralSource
    {
        /// <summary>Initializes a binding.</summary>
        /// <param name="value">What it computes.</param>
        internal ValueBinding(PlanValue value)
        {
            Value = value;
        }

        /// <summary>What it computes.</summary>
        internal PlanValue Value { get; }

        /// <summary>The value's type in the language.</summary>
        internal BoundType Type => Value.Type;

        /// <inheritdoc />
        internal override ImmutableArray<PlanNode> Reads => [Value];
    }

    /// <summary>A column of the host's month table, as a lookup reads it.</summary>
    internal enum CalendarColumn
    {
        /// <summary>The calendar's year.</summary>
        Year,

        /// <summary>The calendar's month, from 1.</summary>
        Month,

        /// <summary>The month's first Gregorian day.</summary>
        StartDate,

        /// <summary>The day after the month's last.</summary>
        EndDate,
    }

    /// <summary>
    ///     One row of a table calendar's month table: the month a date falls in, or the month a
    ///     year and a month number name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Written in one of two places. As a lateral source, it is an outer apply beside the
    ///         statement's joins and bindings, read once per row. Inside a scalar lookup, it is one
    ///         level of a nested subquery, and whatever the level names is bound inside it.
    ///     </para>
    ///     <para>
    ///         A date is sought backward on (calendar, first day), which the host's unique index
    ///         serves with a one-row seek; a year and month are an equality on (calendar, year,
    ///         month). Either finds no row for a date or a month the table does not cover, and so
    ///         answers with the absent value.
    ///     </para>
    /// </remarks>
    internal sealed class CalendarLookup : LateralSource
    {
        /// <summary>Initializes a lookup of the month a date falls in.</summary>
        /// <param name="source">The month table, as the host wrote it.</param>
        /// <param name="calendar">The calendar code, as a bound parameter.</param>
        /// <param name="date">The date, atomic: it is written twice.</param>
        internal CalendarLookup(string source, PlanValue calendar, PlanValue date)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            ArgumentNullException.ThrowIfNull(date);
            Source = source;
            Calendar = calendar;
            Date = date;
        }

        /// <summary>Initializes a lookup of the month a year and a month number name.</summary>
        /// <param name="source">The month table, as the host wrote it.</param>
        /// <param name="calendar">The calendar code, as a bound parameter.</param>
        /// <param name="year">The year.</param>
        /// <param name="month">The month number.</param>
        internal CalendarLookup(string source, PlanValue calendar, PlanValue year, PlanValue month)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            ArgumentNullException.ThrowIfNull(year);
            ArgumentNullException.ThrowIfNull(month);
            Source = source;
            Calendar = calendar;
            Year = year;
            Month = month;
        }

        /// <summary>The month table, as the host wrote it; checked by the schema builder.</summary>
        internal string Source { get; }

        /// <summary>The calendar code, as a bound parameter.</summary>
        internal PlanValue Calendar { get; }

        /// <summary>The date whose month is sought, for a lookup by date.</summary>
        internal PlanValue? Date { get; }

        /// <summary>The year, for a lookup by year and month.</summary>
        internal PlanValue? Year { get; }

        /// <summary>The month number, for a lookup by year and month.</summary>
        internal PlanValue? Month { get; }

        /// <summary>
        ///     The values given a name inside the lookup, where it is a level of a scalar lookup; a
        ///     lateral lookup's are the statement's own.
        /// </summary>
        internal List<ValueBinding> Bindings { get; } = [];

        /// <inheritdoc />
        internal override ImmutableArray<PlanNode> Reads =>
            Date is not null ? [Calendar, Date] : [Calendar, Year!, Month!];
    }

    /// <summary>
    ///     A value computed once before the statement runs.
    /// </summary>
    /// <remarks>
    ///     Reserved for lookups that are the same for every row and expensive to repeat: the
    ///     hierarchy nodes, and the table calendars' answers for operands that read no row.
    ///     Arithmetic over parameters is deliberately left where it is: the backend estimates how
    ///     many rows a comparison will match by looking at a parameter's value, and cannot look
    ///     inside a variable, so hoisting one would buy nothing and cost plan quality.
    /// </remarks>
    internal abstract class HoistedVariable
    {
        /// <summary>Initializes the variable.</summary>
        /// <param name="type">The value's type in the language.</param>
        /// <param name="storeType">The type the variable is declared as.</param>
        private protected HoistedVariable(BoundType type, QueryexStoreType storeType)
        {
            Type = type;
            StoreType = storeType;
        }

        /// <summary>The value's type in the language.</summary>
        internal BoundType Type { get; }

        /// <summary>The type the variable is declared as.</summary>
        internal QueryexStoreType StoreType { get; }

        /// <summary>The name this variable is written under.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>What the variable's initializer reads, which keeps whatever it reads alive.</summary>
        internal abstract PlanNode Reads { get; }
    }

    /// <summary>A hierarchy node looked up once by a key.</summary>
    internal sealed class NodeLookupVariable : HoistedVariable
    {
        /// <summary>Initializes the variable.</summary>
        /// <param name="lookupEntity">The entity a node is looked up in.</param>
        /// <param name="lookupProperty">The property a key is matched against.</param>
        /// <param name="nodeProperty">The hierarchy-node property that is read.</param>
        /// <param name="key">The key to match.</param>
        internal NodeLookupVariable(
            EntityDescriptor lookupEntity,
            PropertyDescriptor lookupProperty,
            PropertyDescriptor nodeProperty,
            PlanValue key)
            : base(BoundType.HierarchyId, StoreTypes.Default(BoundType.HierarchyId, null, 0, 0))
        {
            LookupEntity = lookupEntity;
            LookupProperty = lookupProperty;
            NodeProperty = nodeProperty;
            Key = key;
        }

        /// <summary>The entity a node is looked up in.</summary>
        internal EntityDescriptor LookupEntity { get; }

        /// <summary>The property a key is matched against.</summary>
        internal PropertyDescriptor LookupProperty { get; }

        /// <summary>The hierarchy-node property that is read.</summary>
        internal PropertyDescriptor NodeProperty { get; }

        /// <summary>The key to match.</summary>
        internal PlanValue Key { get; }

        /// <inheritdoc />
        internal override PlanNode Reads => Key;
    }

    /// <summary>A value computed once, by an initializer that reads no row.</summary>
    internal sealed class ComputedVariable : HoistedVariable
    {
        /// <summary>Initializes the variable.</summary>
        /// <param name="value">The initializer: a scalar subquery the declaration evaluates once.</param>
        internal ComputedVariable(PlanValue value)
            : base(value.Type, value.StoreType)
        {
            Value = value;
        }

        /// <summary>The initializer.</summary>
        internal PlanValue Value { get; }

        /// <inheritdoc />
        internal override PlanNode Reads => Value;
    }

    /// <summary>
    ///     The subquery of one semi-join: a join tree, lateral sources and a predicate of its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A subquery reads another entity's rows, so its paths cannot share the statement's
    ///         joins and its bindings and calendar lookups cannot sit in the statement's own source:
    ///         each is computed against a row of the searched entity. The parameters and the hoisted lookups,
    ///         which do not vary by row, stay the statement's.
    ///     </para>
    ///     <para>
    ///         Its aliases continue the statement's sequences rather than starting again, so a name
    ///         used inside never shadows one used outside — which matters, because the predicate inside
    ///         refers to the row outside by its alias.
    ///     </para>
    /// </remarks>
    internal sealed class PlanSubquery
    {
        /// <summary>Initializes a subquery.</summary>
        /// <param name="entity">The entity whose rows it searches.</param>
        /// <param name="origin">Where the semi-join was written, for the join ceiling.</param>
        internal PlanSubquery(EntityDescriptor entity, PlanOrigin origin)
        {
            Root = new JoinNode(entity, null, null, origin) { IsReferenced = true };
            Origin = origin;
        }

        /// <summary>The root of its join tree: the searched entity's own rows.</summary>
        internal JoinNode Root { get; }

        /// <summary>Where the semi-join was written.</summary>
        /// <remarks>
        ///     Its source is a table the statement reads, and counts toward the join ceiling like a
        ///     join does; this is what the problem names when that count is what goes over.
        /// </remarks>
        internal PlanOrigin Origin { get; }

        /// <summary>
        ///     The lateral sources made while lowering the predicate, in the order they were made,
        ///     before the ones nothing reads are dropped.
        /// </summary>
        internal List<LateralSource> Pending { get; } = [];

        /// <summary>The predicate, once lowered: the correlation, then the criterion.</summary>
        internal PlanPredicate? Where { get; set; }

        /// <summary>Whether anything that survived lowering contains this subquery.</summary>
        internal bool IsReferenced { get; set; }

        /// <summary>The joins that survived, in the order their aliases were assigned.</summary>
        internal ImmutableArray<JoinNode> Joins { get; set; } = [];

        /// <summary>The lateral sources that survived, in the order they were made.</summary>
        internal ImmutableArray<LateralSource> Laterals { get; set; } = [];
    }

    /// <summary>One item of the select list, lowered.</summary>
    /// <param name="Value">What it computes.</param>
    /// <param name="Alias">The name the column is written under.</param>
    /// <param name="Column">What the host is told about the column.</param>
    internal sealed record PlanSelectItem(PlanValue Value, string Alias, QueryexColumn Column);

    /// <summary>
    ///     One ordering term, lowered.
    /// </summary>
    /// <remarks>
    ///     A term that matches a select item orders by that item's name rather than by a second copy
    ///     of the expression. That is not a tidiness choice: a grouped statement has to order by the
    ///     very same expression it grouped by, and lowering the term a second time would produce a
    ///     second value binding that the grouping never saw.
    /// </remarks>
    /// <param name="Value">What it orders by, when it is not a select item.</param>
    /// <param name="Alias">The select item's name, when it is one.</param>
    /// <param name="Descending">Whether it orders downward.</param>
    internal sealed record PlanOrderItem(PlanValue? Value, string? Alias, bool Descending);

    /// <summary>
    ///     A whole query, lowered: everything the writer needs and nothing it has to decide.
    /// </summary>
    internal sealed record RelationalPlan
    {
        /// <summary>
        ///     The root of the join tree, or null for a statement with no row source — one that is
        ///     its select list and nothing else, and always answers with exactly one row.
        /// </summary>
        internal required JoinNode? Root { get; init; }

        /// <summary>Every join that survived, in the order their aliases were assigned.</summary>
        internal required ImmutableArray<JoinNode> Joins { get; init; }

        /// <summary>The values computed once before the statement runs.</summary>
        internal required ImmutableArray<HoistedVariable> Variables { get; init; }

        /// <summary>The one-row sources applied to each row, in the order they were made.</summary>
        internal required ImmutableArray<LateralSource> Laterals { get; init; }

        /// <summary>The parameters, in the order they were assigned.</summary>
        internal required ImmutableArray<ParameterSlot> Parameters { get; init; }

        /// <summary>The select list.</summary>
        internal required ImmutableArray<PlanSelectItem> Select { get; init; }

        /// <summary>The row-level predicate, when there is one.</summary>
        internal PlanPredicate? Where { get; init; }

        /// <summary>The grouping keys, when the query groups.</summary>
        internal ImmutableArray<PlanValue> GroupBy { get; init; } = [];

        /// <summary>The group-level predicate, when there is one.</summary>
        internal PlanPredicate? Having { get; init; }

        /// <summary>The ordering terms, tiebreakers included.</summary>
        internal ImmutableArray<PlanOrderItem> OrderBy { get; init; } = [];

        /// <summary>The rows to skip, when the query pages.</summary>
        internal ParameterSlot? Skip { get; init; }

        /// <summary>The rows to take, when the query pages.</summary>
        internal ParameterSlot? Take { get; init; }
    }
}
