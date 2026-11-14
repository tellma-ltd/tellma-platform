// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     Flat rows over one root, as the Excel codec asks the row source for them: the root's columns
    ///     by path, optionally restricted to a key set, with the child-collection queries that ride
    ///     every page.
    /// </summary>
    /// <remarks>
    ///     The row source conjoins the caller's read filter on the root's resource. A dependent's
    ///     <paramref name="Restriction" /> and <paramref name="RestrictionValues" /> are null: the row
    ///     source restricts it to the children of each page's rows.
    /// </remarks>
    /// <param name="RootEntity">The root's entity name (<c>gl.Center</c>): the stack's own, a child collection's or a lookup target's.</param>
    /// <param name="SelectPaths">The columns, by path; each row lists its values in this order.</param>
    /// <param name="OrderBy">The ordering, as query ordering text; null for the source's, else by id.</param>
    /// <param name="Restriction">A path and a table source the row source binds from <paramref name="RestrictionValues" />; null for none.</param>
    /// <param name="RestrictionValues">The key values the restriction binds; null with no restriction.</param>
    /// <param name="Take">The most rows in total; null for no cap of the query's own.</param>
    /// <param name="CountCap">Where the first page's capped count stops; null for no count, and always null on a dependent.</param>
    /// <param name="Dependents">The child-collection queries that ride every page; null for none.</param>
    /// <exception cref="ArgumentException"><paramref name="RootEntity" /> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="SelectPaths" /> is null.</exception>
    public sealed record ExcelQuery(
        string RootEntity,
        IReadOnlyList<string> SelectPaths,
        string? OrderBy,
        KeySetRestriction? Restriction,
        IReadOnlyList<object>? RestrictionValues,
        int? Take,
        int? CountCap,
        IReadOnlyList<ExcelQuery>? Dependents)
    {
        /// <inheritdoc cref="ExcelQuery(string, IReadOnlyList{string}, string?, KeySetRestriction?, IReadOnlyList{object}?, int?, int?, IReadOnlyList{ExcelQuery}?)" />
        public string RootEntity { get; init => field = NotBlank(value, nameof(RootEntity)); } = NotBlank(RootEntity, nameof(RootEntity));

        /// <inheritdoc cref="ExcelQuery(string, IReadOnlyList{string}, string?, KeySetRestriction?, IReadOnlyList{object}?, int?, int?, IReadOnlyList{ExcelQuery}?)" />
        public IReadOnlyList<string> SelectPaths { get; init => field = value ?? throw new ArgumentNullException(nameof(SelectPaths)); } =
            SelectPaths ?? throw new ArgumentNullException(nameof(SelectPaths));

        /// <summary>Refuses a blank entity name.</summary>
        /// <param name="value">The name.</param>
        /// <param name="name">The member's name.</param>
        /// <returns>The name.</returns>
        private static string NotBlank(string value, string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
            return value;
        }
    }
}
