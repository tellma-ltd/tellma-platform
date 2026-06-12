// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Abstractions.TableTypes
{
    // The platform's canonical bulk-operation shapes (spec 0001 §5): each class is the row shape
    // of a standalone SQL Server table type used by dynamic SQL for bulk delete / bulk lookup,
    // and doubles as the DTO bound into the TVP at runtime (e.g. a List<IdList>). Class names
    // deliberately equal the deployed type names. Distributions register them in their
    // composition through the same path as any standalone type:
    //
    //     modelBuilder.HasTableType<IdList>(schema: "dbo", t => t.HasGrants(...));
    //
    // Deliberately annotated with BCL DataAnnotations only — this assembly stays free of EF
    // (and EF-provider) dependencies.

    /// <summary>A row of the <c>[IdList]</c> table type: a single <c>int</c> key.</summary>
    public class IdList
    {
        /// <summary>The key value.</summary>
        [Key]
        public int Id { get; set; }
    }

    /// <summary>A row of the <c>[BigIdList]</c> table type: a single <c>bigint</c> key.</summary>
    public class BigIdList
    {
        /// <summary>The key value.</summary>
        [Key]
        public long Id { get; set; }
    }

    /// <summary>A row of the <c>[GuidList]</c> table type: a single <c>uniqueidentifier</c> key.</summary>
    public class GuidList
    {
        /// <summary>The key value.</summary>
        [Key]
        public Guid Id { get; set; }
    }

    /// <summary>A row of the <c>[StringList]</c> table type: a single <c>nvarchar(450)</c> key.</summary>
    public class StringList
    {
        /// <summary>The key value.</summary>
        [Key]
        [MaxLength(450)]
        public string Id { get; set; } = null!;
    }
}
