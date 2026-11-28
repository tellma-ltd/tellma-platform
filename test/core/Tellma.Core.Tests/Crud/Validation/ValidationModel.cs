// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud.Validation
{
    // The validation suite's model: a widget whose columns carry every facet and attribute the
    // walker judges (a unique code, a bounded name, a decimal(19,4) price, a range, an e-mail whose
    // string length sets its column's, a server-owned column, a class-level rule and an
    // IValidatableObject the walker must ignore), its lines (a parent key with a range nobody
    // judges), a tree, and a part the distribution substitutes with a leaf that widens its code's
    // column. Two confirmation keys are declared.

    /// <summary>Composes the validation suite's model.</summary>
    internal static class ValidationModel
    {
        /// <summary>A declared confirmation key.</summary>
        public const string Backdated = "Vm.Backdated";

        /// <summary>A second declared confirmation key.</summary>
        public const string Overdraft = "Vm.Overdraft";

        /// <summary>Adds the validation model's feature.</summary>
        /// <param name="tellma">The composition's builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma
                .AddFeature(new TestFeature<Tag.C>("validation", contribute: static contribution => contribution
                    .Entity<VmWidget>()
                    .Entity<VmNode>()
                    .Entity<VmPart>()
                    .Model<VmMyPartConfiguration>()
                    .Confirmation(Backdated)
                    .Confirmation(Overdraft)))
                .UseEntity<VmPart, VmMyPart>();
        }
    }

    /// <summary>A rule on the widget as a whole: its code differs from its name.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class VmCodeNotNameAttribute : ValidationAttribute
    {
        /// <inheritdoc />
        public override bool IsValid(object? value)
        {
            return value is not VmWidget widget || !string.Equals(widget.Code, widget.Name, StringComparison.Ordinal);
        }
    }

    /// <summary>The validation model's top-level entity.</summary>
    [Table("Widgets", Schema = "vm")]
    [TableType]
    [VmCodeNotName]
    public class VmWidget : TopLevelEntity, IValidatableObject
    {
        /// <summary>The code, unique on its own.</summary>
        [MaxLength(10)]
        [Unique]
        public string Code { get; set; } = null!;

        /// <summary>The name, required.</summary>
        [MaxLength(20)]
        [Required]
        public string? Name { get; set; }

        /// <summary>The price, <c>decimal(19,4)</c>.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal? Price { get; set; }

        /// <summary>The quantity, from 0 to 100.</summary>
        [Range(0, 100)]
        public int Quantity { get; set; }

        /// <summary>An e-mail address of 6 to 30 characters, the column's length being the string length's maximum.</summary>
        [StringLength(30, MinimumLength = 6)]
        [EmailAddress]
        public string? Email { get; set; }

        /// <summary>A server-owned stamp, whose value a client never sets.</summary>
        [MaxLength(5)]
        [ServerOwned]
        [EmailAddress]
        public string? Stamp { get; set; }

        /// <summary>The lines, at most three per widget.</summary>
        [NotMapped]
        [MaxChildren(3)]
        public List<VmWidgetLine>? Lines { get; set; }

        /// <summary>Always fails, so a test sees that the walker never asks.</summary>
        /// <param name="validationContext">The context.</param>
        /// <returns>One failure.</returns>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return new ValidationResult("The walker never consults IValidatableObject.");
        }
    }

    /// <summary>A widget's line.</summary>
    [Table("WidgetLines", Schema = "vm")]
    [TableType]
    public class VmWidgetLine : ChildEntity
    {
        /// <summary>The widget; the nesting sets it, so its range is never judged on a payload row.</summary>
        [ParentKey]
        [Range(1, int.MaxValue)]
        public int WidgetId { get; set; }

        /// <summary>The units, from 1 to 10.</summary>
        [Range(1, 10)]
        public int Units { get; set; }

        /// <summary>A note.</summary>
        [MaxLength(5)]
        public string? Note { get; set; }
    }

    /// <summary>The validation model's tree.</summary>
    [Table("Nodes", Schema = "vm")]
    [TableType]
    public class VmNode : TreeEntity
    {
        /// <summary>The code.</summary>
        [MaxLength(10)]
        public string Code { get; set; } = null!;
    }

    /// <summary>A part the feature ships, whose code's column is ten characters.</summary>
    [Table("Parts", Schema = "vm")]
    [TableType]
    public class VmPart : TopLevelEntity
    {
        /// <summary>The code, at most ten characters by the feature's attribute.</summary>
        [MaxLength(10)]
        public string Code { get; set; } = null!;
    }

    /// <summary>The distribution's leaf of <see cref="VmPart" />, whose model widens the code's column.</summary>
    public sealed class VmMyPart : VmPart
    {
    }

    /// <summary>Widens the leaf's code column to thirty characters.</summary>
    public sealed class VmMyPartConfiguration : IEntityTypeConfiguration<VmMyPart>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<VmMyPart> builder)
        {
            _ = builder.Property(static part => part.Code).HasMaxLength(30);
        }
    }

    /// <summary>A request body: attributes on its own members, a nested object, a list of them, and a cycle.</summary>
    public sealed class VmBody
    {
        /// <summary>The title, required.</summary>
        [Required]
        public string? Title { get; set; }

        /// <summary>The level, from 1 to 5.</summary>
        [Range(1, 5)]
        public int Level { get; set; }

        /// <summary>A nested item.</summary>
        public VmBodyItem? Item { get; set; }

        /// <summary>Nested items.</summary>
        public List<VmBodyItem> Items { get; set; } = [];

        /// <summary>A reference back, which the walk visits once.</summary>
        public VmBody? Self { get; set; }
    }

    /// <summary>A nested body item.</summary>
    public sealed class VmBodyItem
    {
        /// <summary>A code of at most three characters.</summary>
        [MaxLength(3)]
        public string? Code { get; set; }
    }
}
