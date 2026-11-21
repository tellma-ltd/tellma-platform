// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The application name a deployment's hub takes on a shared Azure SignalR Service instance:
    ///     the deployment id with every hyphen replaced by an underscore, such as
    ///     <c>etpharma_staging</c> for <c>etpharma-staging</c>.
    /// </summary>
    /// <remarks>
    ///     The service prefixes every hub name with the application name, so several deployments
    ///     share one instance without their hubs, groups or server connections ever meeting. The
    ///     service accepts a name that starts with a letter and holds letters, digits and underscores
    ///     alone, which a deployment id written that way always is.
    /// </remarks>
    public static class RealtimeApplicationName
    {
        /// <summary>The application name of a deployment.</summary>
        /// <param name="deployment">The deployment.</param>
        /// <returns>The deployment id with every hyphen replaced by an underscore.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="deployment" /> is null.</exception>
        /// <exception cref="ArgumentException">The deployment id does not start with a letter, which the service requires.</exception>
        public static string From(DeploymentIdentity deployment)
        {
            ArgumentNullException.ThrowIfNull(deployment);

            // A deployment id is lowercase letters, digits and hyphens; only the first character can
            // keep it from being an application name once the hyphens are replaced.
            string id = deployment.DeploymentId;
            return id[0] is >= 'a' and <= 'z'
                ? id.Replace('-', '_')
                : throw new ArgumentException(
                    $"The deployment id '{id}' does not start with a letter, so it cannot name the deployment's application on Azure SignalR Service.",
                    nameof(deployment));
        }
    }
}
