// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the slices table-valued parameter of a notification request: one slice of the
    ///     request's recipients and the arguments of the delivery job that would carry it to the
    ///     channels other than the inbox.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> under its class name. A slice
    ///     covers the inclusive range of <see cref="NotificationRowList.Ordinal" />s from
    ///     <see cref="FirstOrdinal" /> to <see cref="LastOrdinal" />; the statement inserts its job
    ///     only when an active recipient in that range enabled some channel for the type.
    /// </remarks>
    [TableType]
    public class NotificationSliceList
    {
        /// <summary>The slice's position among the request's slices, from 0, <c>int</c>; the key.</summary>
        [Key]
        public int SliceOrdinal { get; set; }

        /// <summary>The first recipient ordinal the slice covers, <c>int</c>.</summary>
        public int FirstOrdinal { get; set; }

        /// <summary>The last recipient ordinal the slice covers, inclusive, <c>int</c>.</summary>
        public int LastOrdinal { get; set; }

        /// <summary>The delivery job's arguments for the slice, as JSON, <c>nvarchar(max)</c>.</summary>
        public string ArgumentsJson { get; set; } = null!;
    }
}
