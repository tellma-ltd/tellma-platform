// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     The operations a stack serves. An operation absent from a stack's set is not projected
    ///     onto any surface, registers no securable, and its service member throws, so a read-only
    ///     stack is declared by its set rather than by a type.
    /// </summary>
    /// <remarks>
    ///     A stack takes its set from <see cref="StackAttribute.Operations" /> on the entity,
    ///     overridden by its registration; the default is <see cref="All" />. <see cref="Enlist" />
    ///     is outside <see cref="All" />: a stack accepts enlisted writes only by naming it.
    /// </remarks>
    [Flags]
    public enum StackOperations
    {
        /// <summary>No operation.</summary>
        None = 0,

        /// <summary><c>query</c>, and <c>get-by-parent-ids</c> on a tree; securable action <c>Read</c>.</summary>
        Query = 1,

        /// <summary><c>get</c>, <c>get-by-ids</c>, and <c>all</c> on a cacheable stack; securable action <c>Read</c>.</summary>
        Details = 2,

        /// <summary><c>save</c>, and <c>activate</c>/<c>deactivate</c> on an activatable stack; securable actions <c>Save</c> and <c>Activate</c>.</summary>
        Save = 4,

        /// <summary><c>delete</c>, <c>delete-by-query</c>, and <c>delete-with-descendants</c> on a tree; securable action <c>Delete</c>.</summary>
        Delete = 8,

        /// <summary>
        ///     The Excel import operations, the export for import among them; requires
        ///     <see cref="Query" />, <see cref="Details" /> and <see cref="Save" />. Securable actions
        ///     <c>Read</c> for the export and <c>Save</c> for the import.
        /// </summary>
        Import = 16,

        /// <summary>The Excel display-shape export operations; requires <see cref="Query" />. Securable action <c>Read</c>.</summary>
        Export = 32,

        /// <summary>
        ///     Accepts enlisted saves and deletes: writes another stack's operation makes in its own
        ///     persist batch. Projects no segment, member or securable.
        /// </summary>
        Enlist = 64,

        /// <summary>The read operations: <see cref="Query" />, <see cref="Details" /> and <see cref="Export" />.</summary>
        Read = Query | Details | Export,

        /// <summary>Every front-door operation: <see cref="Read" />, <see cref="Save" />, <see cref="Delete" /> and <see cref="Import" />.</summary>
        All = Read | Save | Delete | Import,
    }
}
