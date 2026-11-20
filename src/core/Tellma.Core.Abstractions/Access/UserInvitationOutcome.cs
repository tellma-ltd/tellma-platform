// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A row of the table-valued parameter that writes an invitation's outcome back onto the
    ///     invited user: the identity account the user resolved to, the server's answer, or why the
    ///     user was refused.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> as
    ///     <c>UserInvitationOutcomeList</c>. A refused user carries an error and neither a subject nor
    ///     a status.
    /// </remarks>
    [TableType(Name = "UserInvitationOutcomeList")]
    public sealed class UserInvitationOutcome
    {
        /// <summary>The invited user's id; the row's key.</summary>
        [Key]
        public int UserId { get; set; }

        /// <summary>
        ///     The identity account the user's email resolved to, compared in binary as the users'
        ///     subjects are, <c>varchar(255)</c>; null when the user was refused.
        /// </summary>
        [Collation(DatabaseCollations.Binary)]
        [Column(TypeName = "varchar(255)")]
        public string? Subject { get; set; }

        /// <summary>The identity server's answer, as an <see cref="Access.InviteStatus" /> member's name, <c>varchar(9)</c>; null when the user was refused.</summary>
        [Column(TypeName = "varchar(9)")]
        public string? InviteStatus { get; set; }

        /// <summary>Why the user was refused, at most 1,024 characters; null when the user was invited.</summary>
        [MaxLength(1024)]
        public string? LastInviteError { get; set; }
    }
}
