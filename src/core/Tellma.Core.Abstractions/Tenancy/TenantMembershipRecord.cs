// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     One membership hint to record: a subject's user in a tenant, active or not. The row of the
    ///     catalog's table type <c>[catalog].[TenantMembershipList]</c>, bound by column name.
    /// </summary>
    /// <remarks>
    ///     <see cref="Subject" /> is binary-collated, as the catalog's membership table compares it,
    ///     so a subject differing only in case is a different subject.
    /// </remarks>
    [TableType(Name = "TenantMembershipList", Schema = "catalog")]
    public sealed class TenantMembershipRecord
    {
        /// <summary>The tenant's id.</summary>
        public int TenantId { get; set; }

        /// <summary>
        ///     The identity subject, or a service account's client id: printable ASCII without spaces,
        ///     at most 255 characters, compared byte for byte.
        /// </summary>
        [Collation(DatabaseCollations.Binary)]
        [Column(TypeName = "varchar(255)")]
        public required string Subject { get; set; }

        /// <summary>Whether the subject's user in the tenant is active.</summary>
        public bool IsActive { get; set; }
    }
}
