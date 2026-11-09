// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Declares an <see cref="int" /> or <see cref="long" /> property a many-to-one foreign key to
    ///     another entity's table. This is how a pack or a distribution references another entity: it
    ///     writes no model configuration and no navigation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The key targets the table of the leaf registered for <see cref="Target" />, so a reference
    ///         to a pack's default follows the distribution's substitution. The platform's conventions
    ///         name it <c>FK_&lt;Table&gt;_&lt;Column&gt;</c>, with no cascade, and index it with
    ///         <c>IX_&lt;Table&gt;_&lt;Column&gt;</c>. Its query navigation is the column name without its
    ///         <c>Id</c> suffix.
    ///     </para>
    ///     <para>Inherited by a leaf.</para>
    /// </remarks>
    /// <param name="target">The referenced entity: a pack's default, or any entity in the composition.</param>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ReferencesAttribute(Type target) : Attribute
    {
        /// <summary>The referenced entity.</summary>
        public Type Target { get; } = target;
    }
}
