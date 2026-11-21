// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud.ExcelRows
{
    /// <summary>
    ///     The row-source model: a sheet with lines that hold notes, two collections deep, and a tag
    ///     to look up beside it; the sheet's ceilings are tiny, so a test reaches a full chunk at
    ///     every depth with a few rows.
    /// </summary>
    internal static class ExcelRowsModel
    {
        /// <summary>The sheet's resource.</summary>
        public const string Sheets = "xr.XrSheet";

        /// <summary>The sheet line's entity, a child collection.</summary>
        public const string Lines = "xr.XrLine";

        /// <summary>The line note's entity, a grandchild collection.</summary>
        public const string Notes = "xr.XrNote";

        /// <summary>The tag's resource, a lookup target.</summary>
        public const string Tags = "xr.XrTag";

        /// <summary>The sheet's take ceiling, the size of every chunk.</summary>
        public const int MaxTake = 3;

        /// <summary>The sheet's ceiling on the ids a by-ids source names.</summary>
        public const int MaxIds = 4;

        /// <summary>Composes the model's feature.</summary>
        /// <param name="tellma">The builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.D>("rows", contribute: static contribution => contribution
                .Entity<XrSheet>()
                .Entity<XrTag>()));
        }
    }

    /// <summary>A sheet: lines two collections deep, and tiny ceilings.</summary>
    [Table("Sheets", Schema = "xr")]
    [TableType]
    [Stack(MaxTake = ExcelRowsModel.MaxTake, MaxIds = ExcelRowsModel.MaxIds)]
    public class XrSheet : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string? Name { get; set; }

        /// <summary>The lines.</summary>
        [NotMapped]
        public List<XrLine>? Lines { get; set; }
    }

    /// <summary>A sheet's line, with notes of its own.</summary>
    [Table("Lines", Schema = "xr")]
    [TableType]
    public class XrLine : ChildEntity
    {
        /// <summary>The sheet.</summary>
        [ParentKey]
        public int SheetId { get; set; }

        /// <summary>The notes.</summary>
        [NotMapped]
        public List<XrNote>? Notes { get; set; }
    }

    /// <summary>A line's note: the grandchild collection.</summary>
    [Table("Notes", Schema = "xr")]
    [TableType]
    public class XrNote : ChildEntity
    {
        /// <summary>The line.</summary>
        [ParentKey]
        public int LineId { get; set; }

        /// <summary>The text.</summary>
        [MaxLength(255)]
        public string? Text { get; set; }
    }

    /// <summary>A tag, looked up by its code.</summary>
    [Table("Tags", Schema = "xr")]
    [TableType]
    public class XrTag : TopLevelEntity
    {
        /// <summary>The code.</summary>
        [Column(TypeName = "varchar(50)")]
        public string? Code { get; set; }
    }
}
