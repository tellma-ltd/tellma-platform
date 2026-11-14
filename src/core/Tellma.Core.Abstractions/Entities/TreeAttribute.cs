// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Declares the deepest a tree entity's rows may nest. The tree statements cap their recursion
    ///     at it, and a save that would nest deeper is refused.
    /// </summary>
    /// <remarks>
    ///     Optional on a tree entity, whose depth is <see cref="DefaultMaxDepth" /> without it; inherited
    ///     by a leaf. The depth must be between 2 and 32.
    /// </remarks>
    /// <param name="maxDepth">The maximum depth, counting a root as depth 1.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class TreeAttribute(int maxDepth = TreeAttribute.DefaultMaxDepth) : Attribute
    {
        /// <summary>The depth of a tree that declares none, and the largest a declaration may name.</summary>
        public const int DefaultMaxDepth = 32;

        /// <summary>The maximum depth, counting a root as depth 1.</summary>
        public int MaxDepth { get; set; } = maxDepth;
    }
}
