// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Marks a child entity's owning foreign key: the column that pairs the child with the
    ///     <c>[NotMapped]</c> list property its parent carries it in.
    /// </summary>
    /// <remarks>
    ///     A child declares exactly one. The platform's conventions make it a foreign key to the parent's
    ///     table, <c>FK_&lt;Table&gt;_&lt;Column&gt;</c> with no cascade, indexed by
    ///     <c>IX_&lt;Table&gt;_&lt;Column&gt;</c> including the child's key; the save emitter synchronises
    ///     the children of every saved parent through it. A parent key is never written once, derived
    ///     or server-owned: the emitter writes it.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ParentKeyAttribute : Attribute;
}
