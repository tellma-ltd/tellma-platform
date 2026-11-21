// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>A locale member of the caller's profile, as a locale request names it to return it to the tenant's default.</summary>
    public enum MeLocaleMember
    {
        /// <summary>The preferred language.</summary>
        Language,

        /// <summary>The preferred calendar.</summary>
        Calendar,

        /// <summary>The preferred time zone.</summary>
        TimeZone,

        /// <summary>The preferred region, which the regional formats follow.</summary>
        Region,

        /// <summary>The preferred digits.</summary>
        Digits,
    }
}
