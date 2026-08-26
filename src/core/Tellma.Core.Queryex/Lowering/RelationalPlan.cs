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

            // Inner only where the step cannot be missing and nothing above it can be either.
            // Behind an optional step even a mandatory one may have no row, and an inner join there
            // would silently drop rows whose optional ancestor is absent.
            IsInner = parent is null
                || (navigation is not null && navigation.ForeignKey.IsNotNull && parent.IsInner);
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
        internal void MarkReferenced()
        {
            JoinNode? current = this;
            while (current is not null && !current.IsReferenced)
            {
                current.IsReferenced = true;
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
    ///     A value computed once per row and given a name, so the guards that need it more than once
    ///     do not have to recompute it.
    /// </summary>
    /// <param name="Index">Its position among the bindings.</param>
    /// <param name="Value">What it computes.</param>
    /// <param name="Type">The value's type in the language.</param>
    internal sealed record ValueBinding(int Index, PlanValue Value, BoundType Type)
    {
        /// <summary>The alias this binding is written under.</summary>
        internal string Alias { get; set; } = string.Empty;
    }

    /// <summary>
    ///     A value computed once before the statement runs.
    /// </summary>
    /// <remarks>
    ///     Reserved for the hierarchy lookups, which are the same for every row and expensive to
    ///     repeat. Arithmetic over parameters is deliberately left where it is: the backend
    ///     estimates how many rows a comparison will match by looking at a parameter's value, and
    ///     cannot look inside a variable, so hoisting one would buy nothing and cost plan quality.
    /// </remarks>
    /// <param name="Index">Its position among the variables.</param>
    /// <param name="Type">The value's type in the language.</param>
    /// <param name="LookupEntity">The entity a node is looked up in.</param>
    /// <param name="LookupProperty">The property a key is matched against.</param>
    /// <param name="NodeProperty">The hierarchy-node property that is read.</param>
    /// <param name="Key">The key to match.</param>
    internal sealed record HoistedVariable(
        int Index,
        BoundType Type,
        EntityDescriptor LookupEntity,
        PropertyDescriptor LookupProperty,
        PropertyDescriptor NodeProperty,
        PlanValue Key)
    {
        /// <summary>The name this variable is written under.</summary>
        internal string Name { get; set; } = string.Empty;
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
        /// <summary>The root of the join tree.</summary>
        internal required JoinNode Root { get; init; }

        /// <summary>Every join that survived, in the order their aliases were assigned.</summary>
        internal required ImmutableArray<JoinNode> Joins { get; init; }

        /// <summary>The values computed once before the statement runs.</summary>
        internal required ImmutableArray<HoistedVariable> Variables { get; init; }

        /// <summary>The values computed once per row and given a name.</summary>
        internal required ImmutableArray<ValueBinding> Bindings { get; init; }

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
