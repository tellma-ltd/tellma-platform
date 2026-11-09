// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Caps the rows one parent may carry in a child collection: the <c>[NotMapped]</c> list property
    ///     this decorates. A save that exceeds it for any parent, at any depth, is refused before it
    ///     reaches the database.
    /// </summary>
    /// <remarks>A collection without the declaration is capped at <see cref="DefaultCount" />.</remarks>
    /// <param name="count">The most rows one parent may carry in the collection.</param>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class MaxChildrenAttribute(int count) : Attribute
    {
        /// <summary>The cap of a collection that declares none.</summary>
        public const int DefaultCount = 10_000;

        /// <summary>The most rows one parent may carry in the collection.</summary>
        public int Count { get; set; } = count;
    }
}
