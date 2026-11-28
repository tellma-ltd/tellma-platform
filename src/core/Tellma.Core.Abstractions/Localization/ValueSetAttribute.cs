// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     Marks a string property as drawing its values from a value set: a named list of values
    ///     with labels, such as the languages, calendars, time zones or regions. The property is an
    ///     enumeration on every surface (filters, report displays, spreadsheets, the client's
    ///     controls) while its column stays a plain string.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A save refuses a changed value outside the set, matches a value without regard to case
    ///         and stores it as the set spells it. A value stored before the set stopped listing it is
    ///         left alone.
    ///     </para>
    ///     <para>
    ///         The name must be a registered set, and the property a string: the startup checks refuse
    ///         anything else. Inherited by a leaf.
    ///     </para>
    /// </remarks>
    /// <param name="name">The set's name, such as <see cref="CoreValueSets.TimeZone" />.</param>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ValueSetAttribute(string name) : Attribute
    {
        /// <summary>The set's name.</summary>
        public string Name { get; } = name;
    }
}
