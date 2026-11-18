// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud.Save
{
    // The save pipeline suite's model: an activatable document whose columns carry every ownership
    // the pipeline judges (server-owned, write-once, derived, defaulted, multilingual twins, a value
    // set, a display-name prefix, whitespace kept or trimmed), a unique code and a unique write-once
    // serial, references to a guarded category whose read is sensitive, to an unguarded tag, to
    // another document, to a line (a child with a query surface) and to a sub-line (a child
    // without one), lines unique within their document with sub-lines below them, and a tree.

    /// <summary>Composes the save pipeline suite's model.</summary>
    internal static class PipelineSaveModel
    {
        /// <summary>The documents' resource.</summary>
        public const string Documents = "ps.PsDocument";

        /// <summary>The categories' resource.</summary>
        public const string Categories = "ps.PsCategory";

        /// <summary>The tags' entity, which no securable guards.</summary>
        public const string Tags = "ps.PsTag";

        /// <summary>The lines' entity, a child query surface of the documents.</summary>
        public const string Lines = "ps.PsLine";

        /// <summary>The sub-lines' entity, a child with no query surface.</summary>
        public const string SubLines = "ps.PsSubLine";

        /// <summary>The nodes' resource.</summary>
        public const string Nodes = "ps.PsNode";

        /// <summary>The colours' value set.</summary>
        public const string Colors = "ps.Color";

        /// <summary>Adds the model's feature.</summary>
        /// <param name="tellma">The composition's builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.A>("pipeline-save", contribute: static contribution => contribution
                .Entity<PsDocument, PsDocumentService>()
                .Entity<PsCategory>()
                .Entity<PsTag>()
                .Entity<PsNode>()
                .Validator<PsDocument, PsDocumentValidator>()
                .ValueSet(new ValueSetDescriptor(Colors, [new ValueSetEntry("Red", null, "Red"), new ValueSetEntry("Blue", null, "Blue")]))
                .Securables(static securables => securables.MarkSensitive(Categories, AccessActions.Read))));
        }
    }

    /// <summary>The model's document, activatable.</summary>
    [Table("Documents", Schema = "ps")]
    [TableType]
    [DisplayNameSource(nameof(Name), Prefix = nameof(Prefix))]
    public class PsDocument : TopLevelEntity, IActivatable
    {
        /// <summary>The code, unique on its own.</summary>
        [MaxLength(10)]
        [Unique]
        public string? Code { get; set; }

        /// <summary>The name in the primary language.</summary>
        [MaxLength(50)]
        [Multilingual]
        public string Name { get; set; } = null!;

        /// <summary>The name in the secondary language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the tertiary language.</summary>
        public string? Name3 { get; set; }

        /// <summary>The display-name prefix.</summary>
        [MaxLength(10)]
        public string? Prefix { get; set; }

        /// <summary>A note, trimmed.</summary>
        [MaxLength(50)]
        public string? Memo { get; set; }

        /// <summary>A note whose whitespace is kept.</summary>
        [MaxLength(50)]
        [PreserveWhitespace]
        public string? Raw { get; set; }

        /// <summary>A colour from the model's set.</summary>
        [MaxLength(10)]
        [ValueSet(PipelineSaveModel.Colors)]
        public string? Color { get; set; }

        /// <summary>A handler, set once on insert.</summary>
        [MaxLength(10)]
        [WriteOnce]
        public string? Handler { get; set; }

        /// <summary>A serial, set once on insert and unique on its own.</summary>
        [MaxLength(10)]
        [WriteOnce]
        [Unique]
        public string? Serial { get; set; }

        /// <summary>A title the service fills when the client sends none.</summary>
        [MaxLength(50)]
        [Defaulted]
        public string Title { get; set; } = null!;

        /// <summary>A total the service computes.</summary>
        [Derived]
        [Column(TypeName = "decimal(19,4)")]
        public decimal? Total { get; set; }

        /// <summary>A column the server writes.</summary>
        [MaxLength(10)]
        [ServerOwned]
        public string? Audit { get; set; }

        /// <summary>The category.</summary>
        [References(typeof(PsCategory))]
        public int? CategoryId { get; set; }

        /// <summary>The tag, an entity no securable guards.</summary>
        [References(typeof(PsTag))]
        public int? TagId { get; set; }

        /// <summary>Another document.</summary>
        [References(typeof(PsDocument))]
        public int? RelatedId { get; set; }

        /// <summary>A line of a document, a child with a query surface of its own.</summary>
        [References(typeof(PsLine))]
        public int? LineRefId { get; set; }

        /// <summary>A sub-line of a document, a child with no query surface.</summary>
        [References(typeof(PsSubLine))]
        public int? SubLineRefId { get; set; }

        /// <summary>Whether the document is active; left without an initialiser, so only the pipeline makes a new one active.</summary>
        public bool IsActive { get; set; }

        /// <summary>The lines.</summary>
        [NotMapped]
        [MaxChildren(5)]
        public List<PsLine>? Lines { get; set; }
    }

    /// <summary>A document's line.</summary>
    [Table("Lines", Schema = "ps")]
    [TableType]
    public class PsLine : ChildEntity
    {
        /// <summary>The document.</summary>
        [ParentKey]
        public int DocumentId { get; set; }

        /// <summary>A code unique within the document.</summary>
        [MaxLength(10)]
        [Unique]
        public string? LineCode { get; set; }

        /// <summary>The line's category.</summary>
        [References(typeof(PsCategory))]
        public int? CategoryId { get; set; }

        /// <summary>The sub-lines.</summary>
        [NotMapped]
        public List<PsSubLine>? SubLines { get; set; }
    }

    /// <summary>A line's sub-line, with no query surface, so no securable of its own.</summary>
    [Table("SubLines", Schema = "ps")]
    [TableType]
    [ApiResource(Queryable = false)]
    public class PsSubLine : ChildEntity
    {
        /// <summary>The line.</summary>
        [ParentKey]
        public int LineId { get; set; }

        /// <summary>A note.</summary>
        [MaxLength(10)]
        public string? Note { get; set; }
    }

    /// <summary>A category, guarded by its stack's securables, its read sensitive.</summary>
    [Table("Categories", Schema = "ps")]
    [TableType]
    public class PsCategory : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;
    }

    /// <summary>A tag, whose stack declares no operation, so no securable guards it.</summary>
    [Table("Tags", Schema = "ps")]
    [TableType]
    [Stack(Operations = StackOperations.None)]
    public class PsTag : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;
    }

    /// <summary>The model's tree.</summary>
    [Table("Nodes", Schema = "ps")]
    [TableType]
    public class PsNode : TreeEntity
    {
        /// <summary>The code.</summary>
        [MaxLength(10)]
        public string Code { get; set; } = null!;
    }

    /// <summary>The documents' service, whose hooks a test sets.</summary>
    /// <param name="factory">The pipeline factory.</param>
    public sealed class PsDocumentService(IEntityPipelineFactory factory) : EntityService<PsDocument>(factory)
    {
        /// <summary>The preprocessing hook; nothing when null.</summary>
        public Func<SaveContext<PsDocument>, Task>? Preprocess { get; set; }

        /// <summary>The validation hook; nothing when null.</summary>
        public Func<SaveContext<PsDocument>, Task>? Validate { get; set; }

        /// <inheritdoc />
        protected override Task PreprocessAsync(SaveContext<PsDocument> context)
        {
            return Preprocess?.Invoke(context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ValidateAsync(SaveContext<PsDocument> context)
        {
            return Validate?.Invoke(context) ?? Task.CompletedTask;
        }
    }

    /// <summary>What the documents' validator does, which a test sets: shared by the rig's scopes.</summary>
    internal sealed class PsValidation
    {
        /// <summary>The validator's body; nothing when null.</summary>
        public Func<SaveContext<PsDocument>, Task>? Validate { get; set; }
    }

    /// <summary>The documents' validator, whose body a test sets through <see cref="PsValidation" />.</summary>
    /// <param name="validation">The body.</param>
    internal sealed class PsDocumentValidator(PsValidation validation) : IEntityValidator<PsDocument>
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<PsDocument> context)
        {
            return validation.Validate?.Invoke(context) ?? Task.CompletedTask;
        }
    }
}
