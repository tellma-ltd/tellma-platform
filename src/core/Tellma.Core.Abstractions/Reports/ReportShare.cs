// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Sharing;

namespace Tellma.Core.Abstractions.Reports
{
    /// <summary>
    ///     One grant of a stored report: to a user, to a role, or, with neither, to everyone. A mapped
    ///     table beside the reports, never a stack: the sharing actions write it, and a delete of the
    ///     report removes it.
    /// </summary>
    /// <remarks>
    ///     A report holds at most one grant per user, one per role and one Everyone grant, which is
    ///     <see cref="ShareAccess.View" /> only.
    /// </remarks>
    [Table("ReportShares", Schema = "core")]
    [TableType]
    [Temporal]
    public class ReportShare : Entity<int>, IShareRow
    {
        /// <summary>The report the grant belongs to.</summary>
        [References(typeof(Report))]
        public int ReportId { get; set; }

        /// <inheritdoc />
        [References(typeof(User))]
        public int? UserId { get; set; }

        /// <inheritdoc />
        [References(typeof(Role))]
        public int? RoleId { get; set; }

        /// <inheritdoc />
        public ShareAccess Access { get; set; }

        /// <inheritdoc />
        [References(typeof(User))]
        public int SharedById { get; set; }

        /// <inheritdoc />
        public DateTimeOffset SharedAt { get; set; }
    }
}
