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
    ///     A row of the outbox settle's table-valued parameter: the verdict of one outgoing email its
    ///     sending job tried to send.
    /// </summary>
    /// <remarks>A standalone table-type shape, mapped in schema <c>dbo</c> under its class name.</remarks>
    [TableType]
    public class OutgoingEmailOutcomeList
    {
        /// <summary>The outgoing email's id, <c>int</c>; the key.</summary>
        [Key]
        public int Id { get; set; }

        /// <summary>The state the row takes, stored by name, <c>varchar(9)</c>.</summary>
        public OutgoingEmailState State { get; set; }

        /// <summary>The provider's id of the accepted message, <c>nvarchar(255)</c>; null for none.</summary>
        [MaxLength(OutgoingEmail.ProviderMessageIdLength)]
        public string? ProviderMessageId { get; set; }

        /// <summary>Whether the provider reports delivery events for the message, <c>bit</c>.</summary>
        public bool ExpectsDeliveryEvents { get; set; }

        /// <summary>The row's error code, <c>nvarchar(64)</c>; null for none.</summary>
        [MaxLength(OutgoingEmail.ErrorCodeLength)]
        public string? ErrorCode { get; set; }

        /// <summary>The provider's text about the error, <c>nvarchar(1024)</c>; null for none.</summary>
        [MaxLength(OutgoingEmail.ErrorDetailLength)]
        public string? ErrorDetail { get; set; }
    }
}
