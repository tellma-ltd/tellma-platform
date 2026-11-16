// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Validation
{
    // Spec 0015 §6.1 step 1, before any round trip: the payload is non-empty and within MaxSaveCount;
    // every child collection is within its [MaxChildren] per parent, and the payload within
    // MaxRowsPerSave rows, at every depth (beyond any ceiling, LimitExceededException); an id is 0
    // for a new row, negative for a temporary one, positive for an update, and a repeated non-zero
    // id of one entity type is Entity.DuplicateId; then every row's values are walked.
    /// <summary>
    ///     The first step of a save: the payload's shape and ceilings, its ids, and the values of
    ///     every row at every depth.
    /// </summary>
    internal static class SaveShape
    {
        /// <summary>
        ///     Checks a save's payload. A ceiling or a malformed payload throws at once; every other
        ///     failure is recorded, all of them, and the caller raises them together.
        /// </summary>
        /// <param name="entities">The payload's root rows, in payload order.</param>
        /// <param name="metadata">The metadata of the roots' entity.</param>
        /// <param name="limits">The stack's ceilings.</param>
        /// <param name="walker">Checks each row's values.</param>
        /// <param name="errors">Takes every recorded failure.</param>
        /// <exception cref="BadRequestException">The payload is empty, or holds a null row.</exception>
        /// <exception cref="LimitExceededException">The payload is beyond a ceiling.</exception>
        public static void Check(IReadOnlyList<object> entities, EntityMetadata metadata, StackLimits limits, AttributeWalker walker, ValidationErrors errors)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(walker);
            ArgumentNullException.ThrowIfNull(errors);

            // --- The shape and the ceilings, before anything is judged row by row ---
            if (entities.Count == 0)
            {
                throw new BadRequestException("A save carries at least one entity.");
            }

            if (entities.Count > limits.MaxSaveCount)
            {
                throw new LimitExceededException(LimitNames.MaxSaveCount, entities.Count, limits.MaxSaveCount);
            }

            int rows = 0;
            foreach (object? entity in entities)
            {
                rows += Count(entity, metadata);
            }

            if (rows > limits.MaxRowsPerSave)
            {
                throw new LimitExceededException(LimitNames.MaxRowsPerSave, rows, limits.MaxRowsPerSave);
            }

            // --- Every row's id and values, at every depth ---
            Dictionary<EntityMetadata, HashSet<long>> ids = new(ReferenceEqualityComparer.Instance);
            for (int index = 0; index < entities.Count; index++)
            {
                Visit(entities[index], metadata, ValidationPath.Root.Index(index), parentKey: null, walker, ids, errors);
            }
        }

        /// <summary>Counts a row and its descendants, refusing a null row and a collection beyond its ceiling.</summary>
        /// <param name="row">The row.</param>
        /// <param name="metadata">The row's metadata.</param>
        /// <returns>The rows counted.</returns>
        private static int Count(object? row, EntityMetadata metadata)
        {
            if (row is null)
            {
                throw new BadRequestException($"A {metadata.Name} row of the payload is null.");
            }

            int rows = 1;
            foreach (ChildCollectionMetadata collection in metadata.Children)
            {
                // A collection the payload left out is null: its children are left alone.
                if (collection.Getter(row) is not IReadOnlyList<object> children)
                {
                    continue;
                }

                if (children.Count > collection.MaxCount)
                {
                    throw new LimitExceededException(LimitNames.MaxChildren, children.Count, collection.MaxCount);
                }

                foreach (object? child in children)
                {
                    rows += Count(child, collection.Child);
                }
            }

            return rows;
        }

        /// <summary>Checks a row's id and values, then its children's.</summary>
        /// <param name="row">The row.</param>
        /// <param name="metadata">The row's metadata.</param>
        /// <param name="path">The row's path.</param>
        /// <param name="parentKey">The row's parent key when it is a nested child; null for a root.</param>
        /// <param name="walker">Checks the row's values.</param>
        /// <param name="ids">The non-zero ids seen per entity type.</param>
        /// <param name="errors">Takes every failure.</param>
        private static void Visit(
            object row, EntityMetadata metadata, ValidationPath path, PropertyMetadata? parentKey, AttributeWalker walker,
            Dictionary<EntityMetadata, HashSet<long>> ids, ValidationErrors errors)
        {
            // --- 0 is new, below 0 temporary, above 0 an update; a non-zero id repeats nowhere in its entity type ---
            long id = Convert.ToInt64(metadata.Key.Getter(row), CultureInfo.InvariantCulture);
            if (id != 0)
            {
                if (!ids.TryGetValue(metadata, out HashSet<long>? seen))
                {
                    seen = [];
                    ids.Add(metadata, seen);
                }

                if (!seen.Add(id))
                {
                    errors.Add(path.Property(metadata.Key.Name), ValidationCodes.EntityDuplicateId, new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["id"] = id,
                    });
                }
            }

            // --- The row's values, then each child at its own path ---
            walker.CheckRow(row, metadata, path, parentKey, errors);
            foreach (ChildCollectionMetadata collection in metadata.Children)
            {
                if (collection.Getter(row) is not IReadOnlyList<object> children)
                {
                    continue;
                }

                ValidationPath collectionPath = path.Property(collection.Property);
                for (int index = 0; index < children.Count; index++)
                {
                    Visit(children[index], collection.Child, collectionPath.Index(index), collection.ParentKey, walker, ids, errors);
                }
            }
        }
    }
}
