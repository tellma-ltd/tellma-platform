// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud.Query
{
    /// <summary>
    ///     The query model: one stack per search shape, a child surface, a tree with a child surface
    ///     of its own, a service that replaces the search, and two sensitive reads a ticket's paths
    ///     reach: a counter, and a node of the tree.
    /// </summary>
    internal static class QueryModel
    {
        /// <summary>The account's resource.</summary>
        public const string Accounts = "qm.QmAccount";

        /// <summary>The account line's resource, a child query surface.</summary>
        public const string Lines = "qm.QmAccountLine";

        /// <summary>The ticket's resource.</summary>
        public const string Tickets = "qm.QmTicket";

        /// <summary>The counter's resource.</summary>
        public const string Counters = "qm.QmCounter";

        /// <summary>The node's resource.</summary>
        public const string Nodes = "qm.QmNode";

        /// <summary>The node note's resource, a child query surface of a tree.</summary>
        public const string Notes = "qm.QmNodeNote";

        /// <summary>Composes the model's feature.</summary>
        /// <param name="tellma">The builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.D>("query", contribute: static contribution => contribution
                .Entity<QmAccount>()
                .Entity<QmTicket>()
                .Entity<QmCounter, QmCounterService>()
                .Entity<QmNode>()
                .NamedFilters<QmAccount, QmAccountFilters>()
                .Securables(static securables => securables
                    .MarkSensitive(Counters, AccessActions.Read)
                    .MarkSensitive(Nodes, AccessActions.Read))));
        }
    }

    /// <summary>
    ///     An account: activatable, a multilingual searchable name behind a string code prefix, a
    ///     price, and lines; its ceilings are low so a test reaches each.
    /// </summary>
    [Table("Accounts", Schema = "qm")]
    [TableType]
    [DisplayNameSource(nameof(Name), Prefix = nameof(Code))]
    [Stack(MaxTake = 100, MaxSkipWindow = 1_000, MaxSearchLength = 20, CountCap = 500, DefaultOrderBy = "Code")]
    public class QmAccount : TopLevelEntity, IActivatable
    {
        /// <summary>The code, matched from its start.</summary>
        [Column(TypeName = "varchar(50)")]
        [Searchable(SearchKind.Prefix)]
        public string Code { get; set; } = null!;

        /// <summary>The name.</summary>
        [MaxLength(255)]
        [Multilingual]
        [Searchable]
        public string Name { get; set; } = null!;

        /// <summary>The name in the second language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the third language.</summary>
        public string? Name3 { get; set; }

        /// <summary>The price.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal? Price { get; set; }

        /// <summary>Whether the account is active.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>The lines.</summary>
        [NotMapped]
        public List<QmAccountLine>? Lines { get; set; }
    }

    /// <summary>An account's line: a child query surface whose search is the convention's.</summary>
    [Table("AccountLines", Schema = "qm")]
    [TableType]
    public class QmAccountLine : ChildEntity
    {
        /// <summary>The account.</summary>
        [ParentKey]
        public int AccountId { get; set; }

        /// <summary>The name, which the search convention reads.</summary>
        [MaxLength(255)]
        public string? Name { get; set; }

        /// <summary>The amount.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal? Amount { get; set; }
    }

    /// <summary>
    ///     A ticket: a string display name behind an integer prefix, nothing searchable, a counter
    ///     it refers to, and the node of the tree it is filed under.
    /// </summary>
    [Table("Tickets", Schema = "qm")]
    [TableType]
    [DisplayNameSource(nameof(Subject), Prefix = nameof(Number))]
    public class QmTicket : TopLevelEntity
    {
        /// <summary>The number, the display name's prefix.</summary>
        public int Number { get; set; }

        /// <summary>The subject, the display name.</summary>
        [MaxLength(255)]
        public string Subject { get; set; } = null!;

        /// <summary>The counter the ticket refers to, whose read is sensitive.</summary>
        [References(typeof(QmCounter))]
        public int? CounterId { get; set; }

        /// <summary>The node the ticket is filed under, whose read is sensitive.</summary>
        [References(typeof(QmNode))]
        public int? FolderId { get; set; }
    }

    /// <summary>
    ///     A counter: an integer display name, a service whose search replaces the searchable
    ///     columns', and a read marked sensitive.
    /// </summary>
    [Table("Counters", Schema = "qm")]
    [TableType]
    [DisplayNameSource(nameof(Number))]
    public class QmCounter : TopLevelEntity
    {
        /// <summary>The number, the display name.</summary>
        public int Number { get; set; }

        /// <summary>The label, which the service searches.</summary>
        [MaxLength(50)]
        public string? Label { get; set; }
    }

    /// <summary>A node of a tree, whose read is sensitive, with notes.</summary>
    [Table("Nodes", Schema = "qm")]
    [TableType]
    public class QmNode : ActivatableTreeEntity
    {
        /// <summary>The code.</summary>
        [Column(TypeName = "varchar(50)")]
        public string Code { get; set; } = null!;

        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The notes.</summary>
        [NotMapped]
        public List<QmNodeNote>? Notes { get; set; }
    }

    /// <summary>A node's note: a child query surface whose owner is a tree.</summary>
    [Table("NodeNotes", Schema = "qm")]
    [TableType]
    public class QmNodeNote : ChildEntity
    {
        /// <summary>The node.</summary>
        [ParentKey]
        public int NodeId { get; set; }

        /// <summary>The text.</summary>
        [MaxLength(255)]
        public string? Text { get; set; }
    }

    /// <summary>The counter's service: its search reads the label.</summary>
    /// <param name="factory">The pipeline factory.</param>
    public sealed class QmCounterService(IEntityPipelineFactory factory) : EntityService<QmCounter>(factory)
    {
        /// <summary>The search branch the service returns.</summary>
        public const string LabelSearch = "contains(Label, @search)";

        /// <summary>The texts the service was asked about, in order.</summary>
        public List<string> Searched { get; } = [];

        /// <inheritdoc />
        protected override FilterTree? SearchFilter(string search)
        {
            Searched.Add(search);
            return FilterTree.Leaf(LabelSearch);
        }
    }

    /// <summary>The account's named filters.</summary>
    public sealed class QmAccountFilters : INamedFilterContributor<QmAccount>
    {
        /// <summary>The priced filter's text.</summary>
        public const string Priced = "Price > 0";

        /// <summary>A filter that reads a column of the creator outside the user's related projection.</summary>
        public const string ByCreatorEmail = "CreatedBy.Email != ''";

        /// <inheritdoc />
        public IReadOnlyDictionary<string, FilterTree> NamedFilters { get; } = new Dictionary<string, FilterTree>
        {
            ["priced"] = FilterTree.Leaf(Priced),
            ["by-creator-email"] = FilterTree.Leaf(ByCreatorEmail),
        };
    }
}
