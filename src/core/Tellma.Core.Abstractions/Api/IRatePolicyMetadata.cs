// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Endpoint metadata naming one of the <see cref="TellmaRatePolicies" /> an endpoint runs
    ///     under. The web host chains every policy an endpoint carries into one limiter, in a fixed
    ///     order whatever the order of the metadata: the route's own policy first, then
    ///     <see cref="TellmaRatePolicies.User" />, then <see cref="TellmaRatePolicies.Tenant" />. An
    ///     attribute on an action's method that implements it reaches the action's web endpoint.
    /// </summary>
    public interface IRatePolicyMetadata
    {
        /// <summary>The policy's name, one of <see cref="TellmaRatePolicies" />.</summary>
        string Policy { get; }

        /// <summary>
        ///     Whether the request's <c>Tellma-Priority</c> header chooses the lane: true on a report
        ///     run that a dashboard tile may send at low priority. Only
        ///     <see cref="TellmaRatePolicies.Reports" /> has lanes.
        /// </summary>
        bool HonorsPriority { get; }
    }
}
