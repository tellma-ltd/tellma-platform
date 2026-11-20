// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The names of the web host's rate and concurrency policies. Every limit is per instance, and
    ///     no limiter shares state across instances. An endpoint takes a policy through
    ///     <see cref="IRatePolicyMetadata" />: a <see cref="RatePolicyAttribute" /> on an action's
    ///     method, or the endpoint convention of a hand-mapped endpoint.
    /// </summary>
    public static class TellmaRatePolicies
    {
        /// <summary>
        ///     Requests per minute of one user in one tenant, a sliding window. Every web endpoint and
        ///     the blob upload carry it.
        /// </summary>
        public const string User = "tellma-user";

        /// <summary>
        ///     Requests one tenant runs at once, with no queue. Every web endpoint and the blob upload
        ///     carry it, after any route policy and <see cref="User" />.
        /// </summary>
        public const string Tenant = "tellma-tenant";

        /// <summary>
        ///     Anonymous requests per minute of one client address, a fixed window: the information
        ///     document, the health probes, the protected-resource metadata, the OpenAPI document and
        ///     the sign-in.
        /// </summary>
        public const string Anonymous = "tellma-anonymous";

        /// <summary>Exports one user runs at once in one tenant, ahead of <see cref="User" /> and <see cref="Tenant" />.</summary>
        public const string Export = "tellma-export";

        /// <summary>Imports one user runs at once in one tenant, ahead of <see cref="User" /> and <see cref="Tenant" />.</summary>
        public const string Import = "tellma-import";

        /// <summary>
        ///     Reports one user runs at once in one tenant, in two lanes each with its own queue, ahead
        ///     of <see cref="User" /> and <see cref="Tenant" />. A request carrying
        ///     <c>Tellma-Priority: low</c> takes the batch lane on an endpoint whose metadata honors the
        ///     priority; every other request takes the interactive lane.
        /// </summary>
        public const string Reports = "tellma-reports";

        /// <summary>Client telemetry batches per minute of one user, a sliding window apart from <see cref="User" />.</summary>
        public const string Telemetry = "tellma-telemetry";

        /// <summary>
        ///     Blob reads one tenant runs at once, with a queue, in place of <see cref="User" /> and
        ///     <see cref="Tenant" />: a grid of images opens many streams at once, and a browser never
        ///     retries an image it was refused.
        /// </summary>
        public const string Blobs = "tellma-blobs";

        /// <summary>Agent tool calls per minute of one user in one tenant, a sliding window.</summary>
        public const string Mcp = "tellma-mcp";
    }
}
