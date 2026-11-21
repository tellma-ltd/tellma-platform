// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    // The platform's canonical bulk-operation shapes (spec 0001 §5, spec 0012 §4.4): each class is
    // the row shape of a standalone SQL Server table type used for key-set restrictions, bulk
    // deletes and captures, and doubles as the DTO bound into the TVP at runtime (a List<IdList>).
    // Class names equal the types' logical names. CoreFeature contributes every one of them with
    // FeatureContribution.TableType<T>(), and the platform's data context maps each in schema dbo.
    //
    // Annotated with BCL DataAnnotations and the platform's own [TableType] only, so this assembly
    // stays free of EF (and EF-provider) dependencies.

    /// <summary>A row of the <c>[IdList]</c> table type: a single <c>int</c> key.</summary>
    [TableType]
    public class IdList
    {
        /// <summary>The key value.</summary>
        [Key]
        public int Id { get; set; }
    }

    /// <summary>A row of the <c>[BigIdList]</c> table type: a single <c>bigint</c> key.</summary>
    [TableType]
    public class BigIdList
    {
        /// <summary>The key value.</summary>
        [Key]
        public long Id { get; set; }
    }

    /// <summary>A row of the <c>[GuidList]</c> table type: a single <c>uniqueidentifier</c> key.</summary>
    [TableType]
    public class GuidList
    {
        /// <summary>The key value.</summary>
        [Key]
        public Guid Id { get; set; }
    }

    /// <summary>A row of the <c>[StringList]</c> table type: a single <c>nvarchar(450)</c> key.</summary>
    [TableType]
    public class StringList
    {
        /// <summary>The key value.</summary>
        [Key]
        [MaxLength(450)]
        public string Id { get; set; } = null!;
    }
}
