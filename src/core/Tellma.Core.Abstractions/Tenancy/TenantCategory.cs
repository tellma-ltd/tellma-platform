// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Whether a tenant is a business's live books or a sandbox. Fixed when the tenant is
    ///     registered and never changed afterwards.
    /// </summary>
    /// <remarks>
    ///     The catalog stores the member name; the names are part of the catalog's schema and never
    ///     change.
    /// </remarks>
    public enum TenantCategory
    {
        /// <summary>A business's live books: side effects such as email reach real recipients.</summary>
        Live,

        /// <summary>
        ///     A copy for testing, demonstrations or training, whose actions produce no external side
        ///     effects. It may name the live tenant it derives from.
        /// </summary>
        Sandbox,
    }
}
