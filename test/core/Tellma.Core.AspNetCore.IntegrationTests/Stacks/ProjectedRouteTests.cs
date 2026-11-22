// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Api;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;

namespace Tellma.Core.AspNetCore.IntegrationTests.Stacks
{
    /// <summary>
    ///     The routes the reference distribution projects for centers, users and roles and their child
    ///     surfaces are exactly the ones the operation and export cases drive end to end, so a route added to
    ///     one of the three stacks fails here until a case drives it. It covers the stacks' routes
    ///     only: the platform's own service routes (access, notification preferences, settings and
    ///     the rest) are not stack routes and are not held to this list, though the round-trip
    ///     budget case measures the access and notification-preference ones the budget names.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class ProjectedRouteTests(AcmeDeployment deployment)
    {
        /// <summary>The resources whose every route the operation cases drive.</summary>
        private static readonly HashSet<string> Resources = new(StringComparer.Ordinal)
        {
            "gl.Center", "core.User", "core.Role", "core.RoleMembership", "core.Permission",
        };

        /// <summary>
        ///     Every route the operation cases drive, after <c>/{tenantId}/api/web/</c>: the center
        ///     cases, the user cases (the membership's child surface among them), the role cases
        ///     (the permission's child surface among them), and the export cases.
        /// </summary>
        private static readonly string[] Driven =
        [
            "centers/query", "centers/get-by-parent-ids", "centers/get", "centers/get-by-ids", "centers/save", "centers/delete",
            "centers/delete-with-descendants", "centers/activate", "centers/deactivate",
            "users/query", "users/get", "users/get-by-ids", "users/save", "users/delete", "users/activate", "users/deactivate",
            "users/issue-credentials", "users/invite", "users/me", "users/me/save", "users/me/locale", "users/me/preferences/set",
            "users/me/preferences/delete", "users/preferences/get", "users/preferences/set", "users/preferences/delete",
            "users/invitation-status", "users/me/test-notification", "users/notification-preferences/get",
            "users/notification-preferences/save",
            "roles/query", "roles/get", "roles/get-by-ids", "roles/save", "roles/delete", "roles/activate", "roles/deactivate",
            "role-memberships/query", "permissions/query",
            "centers/export", "centers/export-for-import", "users/export", "users/export-for-import", "roles/export", "roles/export-for-import",
            "permissions/export", "role-memberships/export",
        ];

        [Fact]
        public void Every_center_user_and_role_route_the_host_projects_is_driven_end_to_end()
        {
            const string Prefix = "POST /{tenantId}/api/web/";
            IEnumerable<string> projected = deployment.Host.Services.GetRequiredService<IRouteContracts>().Routes
                .Where(static route => route.Resource is string resource && Resources.Contains(resource))
                .Select(static route => route.Template[Prefix.Length..]);

            Assert.Equal(Driven.Order(StringComparer.Ordinal), projected.Order(StringComparer.Ordinal));
        }
    }
}
