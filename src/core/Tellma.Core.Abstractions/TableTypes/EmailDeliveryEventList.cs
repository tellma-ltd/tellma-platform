// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the delivery update's table-valued parameter: the strongest delivery event a
    ///     provider reported of one outgoing email in one webhook call.
    /// </summary>
    /// <remarks>A standalone table-type shape, mapped in schema <c>dbo</c> under its class name.</remarks>
    [TableType]
    public class EmailDeliveryEventList
    {
        /// <summary>The event's position in the call, <c>int</c>; the key.</summary>
        [Key]
        public int Ordinal { get; set; }

        /// <summary>The outgoing email the event concerns, <c>int</c>.</summary>
        public int OutgoingEmailId { get; set; }

        /// <summary>The status the event reports, stored by name, <c>varchar(12)</c>.</summary>
        public EmailDeliveryStatus Status { get; set; }

        /// <summary>The status's strength, <c>tinyint</c>: its value, from 1 for the weakest.</summary>
        public byte Rank { get; set; }

        /// <summary>The address the event concerns, <c>nvarchar(255)</c>; null when the provider named none.</summary>
        [MaxLength(OutgoingEmail.DeliveryRecipientLength)]
        public string? Recipient { get; set; }

        /// <summary>The provider's reason, <c>nvarchar(512)</c>; null for none.</summary>
        [MaxLength(OutgoingEmail.DeliveryReasonLength)]
        public string? Reason { get; set; }

        /// <summary>When the event happened, <c>datetimeoffset(3)</c>; null when the provider gave no time.</summary>
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>The provider's id of the event, <c>nvarchar(100)</c>, which skips its redelivery.</summary>
        [MaxLength(OutgoingEmail.ProviderEventIdLength)]
        public string ProviderEventId { get; set; } = null!;
    }
}
