// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>The configuration keys the host reads outside its options types.</summary>
    internal static class HostConfigurationKeys
    {
        /// <summary>The origin users reach the distribution at, validated by the composition's own check.</summary>
        public const string PublicOrigin = "Tellma:PublicOrigin";

        /// <summary>The Azure SignalR connection string, whose endpoint the SPA's content security policy admits.</summary>
        public const string AzureSignalRConnectionString = "Azure:SignalR:ConnectionString";
    }
}
