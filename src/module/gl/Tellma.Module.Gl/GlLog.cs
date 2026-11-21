// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Module.Gl
{
    // Spec 0018 §8.2's GL event.
    /// <summary>The GL module's structured log events.</summary>
    internal static partial class GlLog
    {
        /// <summary>The sample-centers step wrote its centers into a development tenant.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="count">The centers written, new and updated.</param>
        [LoggerMessage(
            EventName = "SampleCentersProvisioned",
            Level = LogLevel.Information,
            Message = "Wrote {Count} sample centers on tenant {TenantId}.")]
        public static partial void SampleCentersProvisioned(ILogger logger, int tenantId, int count);
    }
}
