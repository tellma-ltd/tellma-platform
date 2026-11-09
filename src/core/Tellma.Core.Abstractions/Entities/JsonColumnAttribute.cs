// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Makes a string property a JSON document column: <c>nvarchar(max)</c>, opaque text to the
    ///     data layer, which binds it as text and leaves it out of the query schema.
    /// </summary>
    /// <remarks>
    ///     The owning service serialises and parses the document. An entity query that asks for every
    ///     mapped property still reads the column. A JSON column cannot be unique or a natural key.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class JsonColumnAttribute : Attribute;
}
