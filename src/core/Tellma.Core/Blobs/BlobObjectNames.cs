// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>The object names a blob row stands for: its primary and one per listed variant.</summary>
    internal static class BlobObjectNames
    {
        /// <summary>The object names of a row.</summary>
        /// <param name="storageKey">The row's primary object name.</param>
        /// <param name="variants">The row's comma-separated variants; empty for none.</param>
        /// <returns>
        ///     The primary name followed by each variant's name, or null when the primary name or a
        ///     variant falls outside the storage grammar, which staging never writes.
        /// </returns>
        public static string[]? Of(string storageKey, string variants)
        {
            ArgumentNullException.ThrowIfNull(storageKey);
            ArgumentNullException.ThrowIfNull(variants);
            if (!BlobName.IsValidPrimary(storageKey))
            {
                return null;
            }

            string[] listed = variants.Length == 0 ? [] : variants.Split(',');
            return listed.All(BlobName.IsValidVariant)
                ? [storageKey, .. listed.Select(variant => BlobName.Variant(storageKey, variant))]
                : null;
        }
    }
}
