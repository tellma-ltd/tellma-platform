// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     The one source of the reserved route words. The server's routing and endpoint audit, the
    ///     client's router and its build tooling all read these lists, so a distribution route or an
    ///     entity segment can never shadow a platform surface or a core page.
    /// </summary>
    /// <remarks>
    ///     A tenant route begins with the tenant's positive integer id, so a tenantless route begins
    ///     with a non-numeric word, and the integer constraint alone keeps the two apart. The lists
    ///     below are the words each position reserves.
    /// </remarks>
    public static class TellmaRoutes
    {
        /// <summary>The segment that names an entity's create page, reserved below every entity segment.</summary>
        public const string NewSegment = "new";

        /// <summary>
        ///     The first segments of the server's tenantless surfaces: <c>api</c>, <c>bff</c>,
        ///     <c>health</c>, <c>id</c>, <c>mcp</c>, <c>openapi</c>, <c>signin-oidc</c>,
        ///     <c>signout-callback-oidc</c> and <c>.well-known</c>.
        /// </summary>
        public static IReadOnlyList<string> ServerFirstSegments { get; } = ReadOnly(
            "api", "bff", "health", "id", "mcp", "openapi", "signin-oidc", "signout-callback-oidc", ".well-known");

        /// <summary>
        ///     The second segments of the server's tenant surfaces, under <c>/{tenantId}</c>:
        ///     <c>api</c>, <c>hub</c>, <c>blobs</c> and <c>mcp</c>.
        /// </summary>
        public static IReadOnlyList<string> ServerTenantSegments { get; } = ReadOnly("api", "hub", "blobs", "mcp");

        /// <summary>The first segments of the client's tenantless pages: <c>tenants</c> and <c>signed-out</c>.</summary>
        public static IReadOnlyList<string> SpaFirstSegments { get; } = ReadOnly("tenants", "signed-out");

        /// <summary>
        ///     The second segments of the client's core tenant pages, under <c>/{tenantId}</c>:
        ///     <c>settings</c>, <c>me</c> and <c>list</c>.
        /// </summary>
        public static IReadOnlyList<string> SpaTenantSegments { get; } = ReadOnly("settings", "me", "list");

        /// <summary>Wraps the words in a read-only list.</summary>
        /// <param name="words">The words.</param>
        /// <returns>The read-only list.</returns>
        private static ReadOnlyCollection<string> ReadOnly(params string[] words)
        {
            // Wrapped so a caller cannot cast the list back to an array and change a reserved word.
            return Array.AsReadOnly(words);
        }
    }
}
