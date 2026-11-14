// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the lease table-valued parameter of a lease renewal: one lease this instance holds,
    ///     and how long to renew it for.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> under its class name. One lease
    ///     covers every job one claim leased.
    /// </remarks>
    [TableType]
    public class JobLeaseList
    {
        /// <summary>The lease token, <c>uniqueidentifier</c>; the key.</summary>
        [Key]
        public Guid Id { get; set; }

        /// <summary>How many seconds from now the renewed lease lasts, <c>int</c>.</summary>
        public int LeaseSeconds { get; set; }
    }
}
