// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the <c>[DateList]</c> table type: a single <c>date</c> key, the list a key-set
    ///     restriction binds for a <see cref="DateOnly" /> natural key.
    /// </summary>
    [TableType]
    public class DateList
    {
        /// <summary>The key value.</summary>
        [Key]
        public DateOnly Id { get; set; }
    }
}
