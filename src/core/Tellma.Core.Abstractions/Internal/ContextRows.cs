// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Abstractions.Internal
{
    /// <summary>
    ///     The payload-row lookups the save, persist and outcome contexts share: a row's before image
    ///     and whether it is an insert, by its payload index.
    /// </summary>
    internal static class ContextRows
    {
        /// <summary>Refuses an index outside the payload.</summary>
        /// <param name="index">The payload index.</param>
        /// <param name="count">The payload's row count.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is negative or not below <paramref name="count" />.</exception>
        public static void CheckIndex(int index, int count)
        {
            if ((uint)index >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    count == 0
                        ? "The payload has no rows, so no row index is valid."
                        : string.Create(CultureInfo.InvariantCulture, $"The payload has {count} row(s); a row index is between 0 and {count - 1}."));
            }
        }

        /// <summary>Whether a payload row is an insert.</summary>
        /// <param name="newRows">The pipeline's insert flags, parallel to the payload; null when the context was not prepared by the pipeline.</param>
        /// <param name="index">The payload index, already checked.</param>
        /// <returns>True for an insert.</returns>
        /// <exception cref="InvalidOperationException">The context carries no insert flags.</exception>
        public static bool IsNew(IReadOnlyList<bool>? newRows, int index)
        {
            return newRows is null
                ? throw new InvalidOperationException("The context does not know which rows are inserts: it was not prepared by the save pipeline.")
                : newRows[index];
        }

        /// <summary>The before images of a context the pipeline prepared after its validation rounds, such as a persist context or an outcome.</summary>
        /// <typeparam name="TEntity">The payload's entity type.</typeparam>
        /// <param name="beforeImages">The before images, parallel to the payload, null for an insert.</param>
        /// <returns>The before images.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="beforeImages" /> is null.</exception>
        public static IReadOnlyList<TEntity?> Loaded<TEntity>(IReadOnlyList<TEntity?>? beforeImages)
        {
            return beforeImages ?? throw new InvalidOperationException(
                "The context carries no before images: it was not prepared by the save pipeline.");
        }

        /// <summary>A payload row's before image.</summary>
        /// <typeparam name="TEntity">The payload's entity type.</typeparam>
        /// <param name="beforeImages">The before images, parallel to the payload, null for an insert; null until the first validation round ran.</param>
        /// <param name="isNew">Whether the row is an insert.</param>
        /// <param name="index">The payload index, already checked.</param>
        /// <returns>The before image, or the default for an insert.</returns>
        /// <exception cref="InvalidOperationException">The row is an update and the before images are not loaded yet.</exception>
        public static TEntity? Before<TEntity>(IReadOnlyList<TEntity?>? beforeImages, bool isNew, int index)
        {
            // An insert has no before image, so it answers before the images are loaded too.
            return isNew
                ? default
                : beforeImages is null
                    ? throw new InvalidOperationException(
                        "The before images load with the first validation round. Await the context loader's LoadAsync before reading an update row's before image.")
                    : beforeImages[index];
        }
    }
}
