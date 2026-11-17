// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     One child collection an editable-shape export writes a sheet for: a collection of the parent,
    ///     or of an enclosing collection, with its members following the request's rules for the
    ///     collection's own entity.
    /// </summary>
    /// <remarks>
    ///     The parent reference, <c>Id</c> and the entity's default natural key are always kept. An
    ///     unknown collection, one repeated among its siblings and a column path that is not editable
    ///     are each a bad request.
    /// </remarks>
    /// <param name="Collection">The collection's property name on its owner (<c>RoleMemberships</c>).</param>
    /// <param name="Columns">The collection's editable column paths to restrict its sheet to; null for all.</param>
    /// <param name="ReferenceKeys">Per foreign-key property of the collection's entity, the target property to express it in; null for the defaults.</param>
    /// <param name="Children">The collections one level down that get a sheet: null for all, empty for none.</param>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "The record names one child collection an export selects, as the entity metadata calls it; it is not itself a collection type.")]
    public sealed record ExportCollection(
        string Collection,
        IReadOnlyList<string>? Columns,
        IReadOnlyDictionary<string, string>? ReferenceKeys,
        IReadOnlyList<ExportCollection>? Children);
}
