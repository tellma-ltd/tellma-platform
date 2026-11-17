// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Access;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The assurance bar sensitive operations require, bound from <c>Tellma:Session:StepUp</c>: the
    ///     bar the access evaluator applies to a sensitive securable, and the one the session asks for
    ///     when it steps up.
    /// </summary>
    public sealed class StepUpOptions
    {
        /// <summary>The authentication context class required; <c>urn:tellma:acr:mfa</c> by default.</summary>
        public string Acr { get; set; } = StepUpBar.DefaultAcr;

        /// <summary>How recently the class must have been reached; 15 minutes by default, null for no limit.</summary>
        public TimeSpan? MaxAge { get; set; } = StepUpBar.DefaultMaxAge;
    }
}
