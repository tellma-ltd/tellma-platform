// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the recipients table-valued parameter of a notification request: one distinct
    ///     recipient, numbered in the order the request first named them.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> under its class name. The
    ///     ordinal both places the recipient in the request's slices and offsets the recipient's row id
    ///     within the id range the insert reserves.
    /// </remarks>
    [TableType]
    public class NotificationRowList
    {
        /// <summary>The recipient's position among the request's distinct recipients, from 0, <c>int</c>; the key.</summary>
        [Key]
        public int Ordinal { get; set; }

        /// <summary>The recipient's user id, <c>int</c>.</summary>
        public int UserId { get; set; }
    }
}
