// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Includes a string property in the entity's free-text search: the search text matches rows
    ///     whose value matches it by <see cref="Kind" />.
    /// </summary>
    /// <param name="kind">How the value matches the search text.</param>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class SearchableAttribute(SearchKind kind = SearchKind.Contains) : Attribute
    {
        /// <summary>How the value matches the search text.</summary>
        public SearchKind Kind { get; set; } = kind;
    }
}
