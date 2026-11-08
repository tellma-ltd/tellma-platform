// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Declares a non-entity service whose <see cref="ApiActionAttribute" /> methods are
    ///     projected under one route segment (<c>inbox</c>, <c>settings</c>, <c>access</c>). Only those
    ///     methods are projected.
    /// </summary>
    /// <remarks>
    ///     The route is unique and is neither a stack segment nor a child query segment. Inherited, so
    ///     a distribution's subclass replacing the service keeps its route.
    /// </remarks>
    /// <example>
    ///     <code>[ApiRoute("access")]</code>
    /// </example>
    /// <param name="route">The route segment the service's operations are mapped under.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ApiRouteAttribute(string route) : Attribute
    {
        /// <summary>The route segment the service's operations are mapped under.</summary>
        public string Route { get; } = route;
    }
}
