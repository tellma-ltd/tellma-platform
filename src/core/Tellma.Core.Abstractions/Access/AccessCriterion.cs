// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A bespoke criterion: a filter that grants an action on a resource's rows to every caller it
    ///     admits, without a stored permission, such as "the users may read their own row".
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Supplied only by the <see cref="IAccessCriteriaProvider" />s registered for the
    ///         resource. A criterion depends on the caller only through <c>me()</c>, <c>now()</c> and
    ///         the other context functions, so one list answers for every user; a rule that varies by
    ///         user says so inside its tree.
    ///     </para>
    ///     <para>
    ///         A criterion can allow access on its own, and never absorbs or removes anything: it adds
    ///         its filter to a decision's disjunction. It applies only to a securable that takes a
    ///         filter.
    ///     </para>
    /// </remarks>
    /// <param name="Action">The action it grants, or the wildcard for every action the resource's securables take a filter on.</param>
    /// <param name="Filter">The rows it admits: a tree of the current Queryex version, over the resource's filter root.</param>
    /// <param name="Reason">A short reason key the provider chooses, such as <c>self</c>, for the "why" answer.</param>
    public sealed record AccessCriterion(string Action, FilterTree Filter, string Reason)
    {
        /// <summary>The action it grants, or the wildcard.</summary>
        public string Action { get; } = string.IsNullOrWhiteSpace(Action)
            ? throw new ArgumentException("A criterion names the action it grants, or the wildcard.", nameof(Action))
            : Action;

        /// <summary>The rows it admits.</summary>
        public FilterTree Filter { get; } = Filter ?? throw new ArgumentNullException(nameof(Filter));

        /// <summary>The reason key.</summary>
        public string Reason { get; } = string.IsNullOrWhiteSpace(Reason)
            ? throw new ArgumentException("A criterion carries a reason key, such as 'self'.", nameof(Reason))
            : Reason;
    }
}
