// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data.Entities
{
    /// <summary>
    ///     One fixed-window rate-limit counter (email-code issuance, user-code attempts, pass
    ///     attempts). SQL-backed behind an interface seam so a distributed cache can substitute
    ///     later as a configuration change.
    /// </summary>
    public sealed class RateLimitCounter
    {
        /// <summary>The counter key, for example <c>emailcode:user:&lt;id&gt;</c>.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The start of the fixed window this row counts.</summary>
        public DateTimeOffset WindowStartUtc { get; set; }

        /// <summary>The number of events observed in the window.</summary>
        public int Count { get; set; }
    }
}
