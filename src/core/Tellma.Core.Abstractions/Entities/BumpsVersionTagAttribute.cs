// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes every write to the entity's table bump a tenant-level version tag, which invalidates
    ///     the caches validated by that tag: a pack's own caches, or the platform's settings and
    ///     permissions.
    /// </summary>
    /// <remarks>
    ///     Repeatable, one tag per declaration, and inherited by a leaf. The <c>entity:</c> tags of
    ///     cacheable entities are implied by <see cref="CacheableAttribute" /> and never declared by
    ///     hand.
    /// </remarks>
    /// <param name="name">The tag name.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class BumpsVersionTagAttribute(string name) : Attribute
    {
        /// <summary>The tag name.</summary>
        public string Name { get; } = name;
    }
}
