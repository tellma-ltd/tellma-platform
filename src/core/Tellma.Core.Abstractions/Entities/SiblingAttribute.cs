// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes an entity a one-row-per-owner sibling of another entity: its key is its owner's key,
    ///     and the owner reaches it through a one-to-one query navigation. This is how a pack owns data
    ///     about an entity it does not own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The key is a foreign key to the table of the leaf registered for <see cref="Owner" />,
    ///         <c>FK_&lt;Table&gt;_Id</c>, which cascades the owner's delete; the table has no sequence,
    ///         since its writer inserts the owner's id. The feature that owns the sibling writes and
    ///         reads it.
    ///     </para>
    ///     <para>Inherited by a leaf.</para>
    /// </remarks>
    /// <param name="owner">The owning entity: a pack's default, or any entity in the composition.</param>
    /// <param name="navigation">The name of the owner's query navigation to the sibling.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class SiblingAttribute(Type owner, string navigation) : Attribute
    {
        /// <summary>The owning entity.</summary>
        public Type Owner { get; } = owner;

        /// <summary>The name of the owner's query navigation to the sibling.</summary>
        public string Navigation { get; } = navigation;
    }
}
