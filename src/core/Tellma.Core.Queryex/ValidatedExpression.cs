// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     One validated item of an expression list.
    /// </summary>
    /// <param name="Span">The item's range in the input text.</param>
    /// <param name="Type">The item's static type.</param>
    /// <param name="Nullity">Whether the item can evaluate to an absent value.</param>
    /// <param name="Direction">The direction suffix, when directions are enabled and one was written.</param>
    /// <param name="UsesAggregation">True when the item contains an aggregation.</param>
    public sealed record ValidatedItem(
        QueryexSpan Span,
        QueryexType Type,
        QueryexNullity Nullity,
        QueryexDirection Direction,
        bool UsesAggregation);

    /// <summary>
    ///     The outcome of validating an expression list without emitting SQL.
    /// </summary>
    /// <remarks>
    ///     Validation exists so that stored expressions — report definitions, access-control
    ///     criteria — are checked at save time by the same binder that will compile them at run
    ///     time, rather than by a second, drift-prone validation path.
    ///     <para>
    ///         It deliberately returns no SQL. Fragments compiled in isolation cannot share joins,
    ///         aliases, or parameters with the query they would eventually join, so a public
    ///         fragment-SQL entry point would be an invitation to concatenate — the exact failure
    ///         <see cref="FilterTree" /> exists to prevent.
    ///     </para>
    /// </remarks>
    /// <param name="Items">The validated items, in source order.</param>
    public sealed record ValidatedExpression(IReadOnlyList<ValidatedItem> Items);
}
