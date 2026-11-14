// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Declares, on an entity type, the navigations a details response loads into its related
    ///     entities. Without it, every foreign-key navigation of the entity and of its children is
    ///     loaded at depth one. Inherited.
    /// </summary>
    /// <remarks>
    ///     A navigation reaches at most the stack's expand depth; the startup gate validates each
    ///     against the tenant-independent schema.
    /// </remarks>
    /// <example>
    ///     <code>[DetailsExpand("Parent", "CreatedBy")]</code>
    /// </example>
    /// <param name="navigations">The navigation paths.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class DetailsExpandAttribute(params string[] navigations) : Attribute
    {
        /// <summary>The navigation paths, in declaration order.</summary>
        public IReadOnlyList<string> Navigations { get; } = ReadOnlyCopies.Of(navigations ?? []);
    }
}
