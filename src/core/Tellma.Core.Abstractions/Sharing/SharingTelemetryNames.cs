// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     The names of the sharing model's instruments and tags. A tag carries a resource name, never
    ///     a tenant, a user or a row, which belong to the request span and the structured log.
    /// </summary>
    public static class SharingTelemetryNames
    {
        /// <summary>The meter every instrument below is created on.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>Histogram of the audience users of each audience resolved, by resource.</summary>
        public const string AudienceMembers = "tellma.sharing.audience.members";

        /// <summary>The tag of the shareable resource whose audience was resolved.</summary>
        public const string ResourceTag = "resource";
    }
}
