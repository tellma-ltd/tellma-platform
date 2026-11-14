// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Declares the details extras a class produces: named result sets beside the main rows that
    ///     a details request selects by name. Placed on an entity service or a details contributor.
    /// </summary>
    /// <remarks>
    ///     A class's extras are the names on it and on its base classes, so the attribute is
    ///     inherited and may repeat; the platform reads every instance. The names are read once at
    ///     startup, never from a scoped instance. The platform's own extra <c>permissions</c> is never
    ///     declared here.
    /// </remarks>
    /// <example>
    ///     <code>[DetailsExtras("members", "problems")]</code>
    /// </example>
    /// <param name="names">The extras' names.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class DetailsExtrasAttribute(params string[] names) : Attribute
    {
        /// <summary>The extras' names, in declaration order.</summary>
        public IReadOnlyList<string> Names { get; } = ReadOnlyCopies.Of(names ?? []);
    }
}
