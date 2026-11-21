// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Composition;
using Tellma.Core.Tenancy;
using Tellma.Defaults.Azure.AspNetCore;
using Tellma.Distro.Acme.Web.Provisioning;

namespace Tellma.Distro.Acme.Web
{
    /// <summary>
    ///     What only Acme's web host composes, after <see cref="AcmeComposition.Compose" />: never a
    ///     feature or an entity, which the migrator would not see.
    /// </summary>
    public static class AcmeWebComposition
    {
        /// <summary>
        ///     Composes the web host's additions: the Azure web defaults and, in Development, the
        ///     provisioning trigger that runs the distribution's own migrator.
        /// </summary>
        /// <param name="tellma">The composition builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            ArgumentNullException.ThrowIfNull(tellma);
            _ = tellma.UseAzureWebDefaults();

            // --- Self-serve provisioning: a local migrator process in Development, none elsewhere ---
            // Replaces the platform's default, which answers the admin surface's tenant POST 501.
            if (tellma.Environment.IsDevelopment())
            {
                tellma.Services.Replace(ServiceDescriptor.Singleton<ITenantProvisioningTrigger, LocalMigratorProvisioningTrigger>());
            }
        }
    }

    /// <summary>
    ///     Names the web host's assembly for a host built in a test, such as by
    ///     <c>WebApplicationFactory</c>; it has no other use.
    /// </summary>
    public sealed class AcmeWebHostMarker
    {
    }
}
