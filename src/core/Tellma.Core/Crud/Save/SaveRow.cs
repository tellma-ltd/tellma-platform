// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Ids;

namespace Tellma.Core.Crud.Save
{
    /// <summary>
    ///     One row of a save payload at any depth, as the pipeline walks it: the row, where it sits,
    ///     what it was before the save touched it, and what it is in the database.
    /// </summary>
    /// <param name="rules">The rules of the row's entity.</param>
    /// <param name="row">The row.</param>
    /// <param name="path">The row's path: <c>[i]</c> for a root, <c>[i].Lines[j]</c> for a child.</param>
    /// <param name="rootIndex">The position of the row's root in the payload.</param>
    /// <param name="owner">The row whose collection holds it; null for a root.</param>
    /// <param name="collection">The collection that holds it; null for a root.</param>
    internal sealed class SaveRow(RowRules rules, object row, ValidationPath path, int rootIndex, SaveRow? owner, ChildCollectionMetadata? collection)
    {
        /// <summary>The rules of the row's entity.</summary>
        public RowRules Rules { get; } = rules;

        /// <summary>The row's entity.</summary>
        public EntityMetadata Entity => Rules.Entity;

        /// <summary>The row.</summary>
        public object Row { get; } = row;

        /// <summary>The row's path.</summary>
        public ValidationPath Path { get; } = path;

        /// <summary>The position of the row's root in the payload.</summary>
        public int RootIndex { get; } = rootIndex;

        /// <summary>The row whose collection holds it; null for a root.</summary>
        public SaveRow? Owner { get; } = owner;

        /// <summary>The collection that holds it; null for a root.</summary>
        public ChildCollectionMetadata? Collection { get; } = collection;

        /// <summary>The row's id as the payload carried it: zero or a temporary id for a new row, positive for an update.</summary>
        public long OriginalId { get; } = PayloadRows.KeyOf(rules.Entity, row);

        /// <summary>Whether the row is new: its payload id was zero or temporary.</summary>
        public bool IsNew => OriginalId <= 0;

        /// <summary>The row's current id: the payload's for an update, the assigned one for a new row once ids are assigned.</summary>
        public long Id => PayloadRows.KeyOf(Entity, Row);

        /// <summary>The row's children in every collection, in collection order; each knows its collection.</summary>
        public List<SaveRow> Children { get; } = [];

        /// <summary>
        ///     The row as stored, loaded under the caller's read filter with its present collections;
        ///     null for a new row, and for an update row whose stored row was not found.
        /// </summary>
        public object? Before { get; set; }

        /// <summary>The path of one of the row's properties.</summary>
        /// <param name="property">The property's name.</param>
        /// <returns>The path.</returns>
        public ValidationPath At(string property)
        {
            return Path.Property(property);
        }
    }
}
