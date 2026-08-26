// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>Where a parameter slot's execution-time value comes from.</summary>
    public enum QueryexParameterOrigin
    {
        /// <summary>
        ///     A value fixed at compile time — an expression literal, a resolved zone name, or a
        ///     paging value. The slot carries it.
        /// </summary>
        Literal,

        /// <summary>The current date in the tenant's time zone.</summary>
        Today,

        /// <summary>The current instant.</summary>
        Now,

        /// <summary>The current user's identifier.</summary>
        UserId,

        /// <summary>The tenant's time zone, as the backend's own name for it.</summary>
        TimeZone,

        /// <summary>A parameter the caller declared. The slot names it.</summary>
        Declared,
    }

    /// <summary>
    ///     One parameter of a compiled query.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every literal, every context value, and every declared parameter reaches the backend
    ///         through one of these. No user-derived value is ever interpolated into SQL text.
    ///     </para>
    ///     <para>
    ///         The host binds each slot before executing: literals from <see cref="Value" />, the
    ///         context slots from its own clock, user, and tenant, and declared slots from whatever
    ///         was supplied for <see cref="DeclaredName" />. A declared parameter compared against
    ///         columns of different store types yields one slot per store type, all carrying the
    ///         same declared name and all bound from the one supplied value.
    ///     </para>
    /// </remarks>
    /// <param name="Name">The parameter name as it appears in the SQL.</param>
    /// <param name="Type">The slot's type in the language.</param>
    /// <param name="StoreType">
    ///     The SQL type to bind the parameter as. A slot compared against a column takes that
    ///     column's type <i>family</i> and nothing else — never its size, precision, or scale, since
    ///     a data provider silently truncates a string to the declared size and rounds a decimal to
    ///     the declared scale, which would compare a mangled value. A parameter wider than its
    ///     column costs nothing, because the backend converts the parameter side only.
    /// </param>
    /// <param name="Origin">Where the value comes from.</param>
    /// <param name="Value">The value, for literal slots. Null for every other origin.</param>
    /// <param name="DeclaredName">The declared parameter's name, for declared slots.</param>
    public sealed record QueryexParameterSlot(
        string Name,
        QueryexType Type,
        QueryexStoreType StoreType,
        QueryexParameterOrigin Origin,
        object? Value = null,
        string? DeclaredName = null);
}
