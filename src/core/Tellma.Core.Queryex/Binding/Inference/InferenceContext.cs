// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Binding.Inference
{
    /// <summary>
    ///     One parameter whose type is being inferred rather than declared.
    /// </summary>
    /// <remarks>
    ///     Variables are merged rather than solved one at a time, so a use that only links two
    ///     parameters to each other still pays off when one of them is later pinned down by
    ///     something else. That is the case a per-clause pass cannot recover: with one clause
    ///     equating two parameters and another dating one of them, the second is datable only
    ///     through the first, and a link discarded at the clause boundary is gone for good.
    /// </remarks>
    internal sealed class InferenceVariable
    {
        /// <summary>The representative of this variable's class, once merged.</summary>
        private InferenceVariable? _parent;

        /// <summary>Initializes a variable.</summary>
        /// <param name="name">The parameter name, in the casing first seen.</param>
        internal InferenceVariable(string name)
        {
            Name = name;
        }

        /// <summary>The parameter name, in the casing first seen.</summary>
        internal string Name { get; }

        /// <summary>Every occurrence's site, in the order they were found.</summary>
        internal List<QueryexTextSite> Occurrences { get; } = [];

        /// <summary>The solved type, once a use demanded one.</summary>
        internal BoundType? Solved { get; set; }

        /// <summary>The site of the use that solved it.</summary>
        internal QueryexTextSite? SolvedAt { get; set; }

        /// <summary>Every demand that could not be reconciled with the solved type.</summary>
        internal List<TypeConflict> Conflicts { get; } = [];

        /// <summary>The representative of this variable's class.</summary>
        /// <returns>The representative.</returns>
        internal InferenceVariable Find()
        {
            InferenceVariable current = this;
            while (current._parent is not null)
            {
                // Path compression: long chains would otherwise build up across a whole query's
                // clauses, and this runs on every occurrence.
                current._parent = current._parent._parent ?? current._parent;
                current = current._parent;
            }

            return current;
        }

        /// <summary>Records that a use demands a type.</summary>
        /// <param name="demanded">The type the use demands.</param>
        /// <param name="site">Where the use is.</param>
        /// <remarks>
        ///     Never fails the bind. An incompatible demand becomes a recorded conflict and binding
        ///     carries on, so the other independent problems in the same expression are still found.
        /// </remarks>
        internal void Demand(BoundType demanded, QueryexTextSite site)
        {
            if (demanded is BoundType.Null or BoundType.Error)
            {
                return;
            }

            InferenceVariable root = Find();
            if (root.Solved is null)
            {
                root.Solved = demanded;
                root.SolvedAt = site;
                return;
            }

            if (root.Solved != demanded)
            {
                root.Conflicts.Add(new TypeConflict(BoundTypes.ToPublic(demanded), site));
            }
        }

        /// <summary>Records where a parameter was written.</summary>
        /// <param name="site">The site.</param>
        internal void Occur(QueryexTextSite site)
        {
            Occurrences.Add(site);
        }

        /// <summary>Merges another variable's class into this one's.</summary>
        /// <param name="other">The variable to merge.</param>
        internal void Union(InferenceVariable other)
        {
            InferenceVariable left = Find();
            InferenceVariable right = other.Find();
            if (ReferenceEquals(left, right))
            {
                return;
            }

            right._parent = left;

            if (left.Solved is null && right.Solved is not null)
            {
                left.Solved = right.Solved;
                left.SolvedAt = right.SolvedAt;
            }
            else if (left.Solved is not null && right.Solved is not null && left.Solved != right.Solved)
            {
                left.Conflicts.Add(new TypeConflict(
                    BoundTypes.ToPublic(right.Solved.Value),
                    right.SolvedAt ?? new QueryexTextSite(null, default)));
            }

            left.Conflicts.AddRange(right.Conflicts);
            right.Conflicts.Clear();
        }
    }

    /// <summary>
    ///     One query's shared inference state.
    /// </summary>
    /// <remarks>
    ///     Held across every clause, because a parameter is referenced from several of them and the
    ///     uses have to be seen together. Inference never guesses whether a value may be absent —
    ///     nothing in an expression constrains that — so an inferred parameter is proposed as
    ///     possibly absent, which is the safe direction, and the author marks it required.
    /// </remarks>
    internal sealed class InferenceContext
    {
        /// <summary>The variables, by parameter name.</summary>
        private readonly Dictionary<string, InferenceVariable> _variables =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The order names were first seen, so results are reported deterministically.</summary>
        private readonly List<string> _order = [];

        /// <summary>The variable for a name, creating it if this is its first use.</summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>The variable.</returns>
        internal InferenceVariable GetOrCreate(string name)
        {
            if (_variables.TryGetValue(name, out InferenceVariable? existing))
            {
                return existing;
            }

            InferenceVariable created = new(name);
            _variables.Add(name, created);
            _order.Add(name);
            return created;
        }

        /// <summary>The solved type of a parameter, when its class has one.</summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>The type, or null.</returns>
        internal BoundType? SolvedType(string name)
        {
            return _variables.TryGetValue(name, out InferenceVariable? variable)
                ? variable.Find().Solved
                : null;
        }

        /// <summary>Every parameter, in the order its name was first seen.</summary>
        /// <returns>The results.</returns>
        internal IEnumerable<ParameterUse> Solve()
        {
            foreach (string name in _order)
            {
                InferenceVariable variable = _variables[name];
                InferenceVariable root = variable.Find();
                bool conflicting = root.Conflicts.Count > 0;

                List<TypeConflict> conflicts = [];
                if (conflicting)
                {
                    // The type that was settled on first is one of the demands in conflict, so it
                    // belongs in the list alongside the ones that disagreed with it.
                    if (root.Solved is BoundType solved && root.SolvedAt is QueryexTextSite site)
                    {
                        conflicts.Add(new TypeConflict(BoundTypes.ToPublic(solved), site));
                    }

                    conflicts.AddRange(root.Conflicts);
                }

                yield return new ParameterUse(
                    name,
                    variable.Occurrences,
                    conflicting || root.Solved is null
                        ? null
                        : BoundTypes.ToPublic(root.Solved.Value),
                    conflicts);
            }
        }
    }
}
