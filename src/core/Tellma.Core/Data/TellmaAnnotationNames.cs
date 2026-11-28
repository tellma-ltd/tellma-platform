// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Data
{
    /// <summary>The model annotations the platform's conventions record for the data layer to read back.</summary>
    internal static class TellmaAnnotationNames
    {
        /// <summary>On a tree entity type: the tree's maximum depth, an <see cref="int" />.</summary>
        public const string TreeMaxDepth = "Tellma:Tree:MaxDepth";

        /// <summary>
        ///     On a platform table that is not an entity: the query language's name for it, a
        ///     <see cref="string" /> such as <c>core.UserStamp</c>. The query schema exposes such a table
        ///     read-only, with a navigation for each of its foreign keys, and no surface ever writes it.
        /// </summary>
        public const string QueryexReadOnlyEntity = "Tellma:Queryex:ReadOnlyEntity";
    }
}
