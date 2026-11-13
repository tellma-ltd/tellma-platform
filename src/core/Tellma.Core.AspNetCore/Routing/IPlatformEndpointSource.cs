// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>
    ///     Endpoints a host bundle contributes to the platform's own tenantless routes, such as the
    ///     webhook endpoint under <c>/api/webhooks</c>. <c>MapTellma</c> maps every registered source
    ///     and stamps what it maps as the platform's: anonymous, tenantless and exempt from the
    ///     audit's rules on a distribution's route words.
    /// </summary>
    /// <remarks>
    ///     Internal, so only the platform's own bundles can mark an endpoint as the platform's; a
    ///     distribution maps its endpoints onto the groups <c>MapTellma</c> returns, under the audit.
    /// </remarks>
    internal interface IPlatformEndpointSource
    {
        /// <summary>Why the endpoints serve no tenant, recorded on each as its tenantless reason.</summary>
        string Reason { get; }

        /// <summary>Maps the source's endpoints.</summary>
        /// <param name="endpoints">The application's route builder.</param>
        /// <returns>The convention builder of each endpoint mapped, which the platform stamps.</returns>
        IReadOnlyList<IEndpointConventionBuilder> Map(IEndpointRouteBuilder endpoints);
    }
}
