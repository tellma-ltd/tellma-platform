// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     What the provider's delivery events last said of a sent email, ordered from the weakest to
    ///     the strongest: a row only ever moves to a stronger status, or to a later event of the same
    ///     one. Stored by name, <c>varchar(12)</c>.
    /// </summary>
    public enum EmailDeliveryStatus
    {
        /// <summary>The receiving server deferred the message; the provider keeps trying.</summary>
        Deferred = 1,

        /// <summary>The receiving server accepted the message.</summary>
        Delivered,

        /// <summary>The provider dropped the message without trying, such as for a suppressed address.</summary>
        Dropped,

        /// <summary>The provider gave up delivering the message.</summary>
        Failed,

        /// <summary>The receiving server bounced the message.</summary>
        Bounced,

        /// <summary>The recipient reported the message as spam.</summary>
        SpamReported,
    }
}
