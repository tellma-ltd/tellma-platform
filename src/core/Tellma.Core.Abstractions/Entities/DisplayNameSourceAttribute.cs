// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Names the property a row's display name renders, the one string that stands for any row of
    ///     the entity in a navigation cell, an Excel key column, a report member or a notification, and
    ///     an optional prefix rendered before it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without the declaration, the display name is the entity's multilingual <c>Name</c> group,
    ///         else its first natural key, else its key. The property names a multilingual anchor or a
    ///         string, integer or date scalar of the entity; the prefix names a string or integer
    ///         scalar. A prefix and a name join with a fixed separator, <c>" - "</c>.
    ///     </para>
    ///     <para>
    ///         Inherited by a leaf. <c>[DisplayNameSource(nameof(Name), Prefix = nameof(Code))]</c> on an
    ///         account renders the row <c>{ Code: "1101", Name: "Cash" }</c> as <c>1101 - Cash</c>.
    ///     </para>
    /// </remarks>
    /// <param name="property">The property the display name renders.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class DisplayNameSourceAttribute(string property) : Attribute
    {
        /// <summary>The property the display name renders.</summary>
        public string Property { get; } = property;

        /// <summary>The property rendered before it, if any.</summary>
        public string? Prefix { get; set; }
    }
}
