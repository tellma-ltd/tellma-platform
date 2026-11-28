// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Distro.Acme.Entities
{
    /// <summary>What kind of buyer a customer is; stored and sent as the member's name.</summary>
    public enum CustomerType
    {
        /// <summary>A shop or an individual buying for its own use.</summary>
        Retail,

        /// <summary>A company buying on account.</summary>
        Corporate,

        /// <summary>A public body buying under its procurement rules.</summary>
        Government,
    }
}
