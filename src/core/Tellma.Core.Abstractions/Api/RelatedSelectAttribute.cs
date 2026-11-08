// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Declares, on an entity type, the columns visible when the entity is reached through a
    ///     navigation: what a details response's related entities carry, and the most a query may
    ///     read across a navigation into the entity under a filtered or absent read grant. Without it
    ///     the projection is the narrow default: the id, the multilingual name group, the code when
    ///     present and every avatar blob reference. Inherited.
    /// </summary>
    /// <remarks>
    ///     A declaration replaces the default and must keep the id, the display-name columns and the
    ///     code; the startup gate refuses one that does not, and validates the text against the
    ///     tenant-independent schema.
    /// </remarks>
    /// <example>
    ///     <code>[RelatedSelect("Id,HandlerKey,Status,ProgressPercent,LabelKey,LabelArgumentsJson,CompletedAt")]</code>
    /// </example>
    /// <param name="select">The Queryex select text of the projection, comma-separated.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class RelatedSelectAttribute(string select) : Attribute
    {
        /// <summary>The Queryex select text of the projection, comma-separated.</summary>
        public string Select { get; } = select;
    }
}
