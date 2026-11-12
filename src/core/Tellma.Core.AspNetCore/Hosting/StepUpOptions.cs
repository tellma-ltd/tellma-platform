// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>The assurance bar sensitive operations require, bound from <c>Tellma:Session:StepUp</c>.</summary>
    public sealed class StepUpOptions
    {
        /// <summary>The authentication context class required; <c>urn:tellma:acr:mfa</c> by default.</summary>
        public string Acr { get; set; } = "urn:tellma:acr:mfa";

        /// <summary>How recently the class must have been reached; 15 minutes by default, null for no limit.</summary>
        public TimeSpan? MaxAge { get; set; } = TimeSpan.FromMinutes(15);
    }
}
