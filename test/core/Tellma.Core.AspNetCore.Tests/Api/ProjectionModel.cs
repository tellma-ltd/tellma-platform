// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Api
{
    // The projection suite's model: an activatable tree with a child collection and an action of
    // every shape, a stack keyed by long, a cacheable stack, and a routed service. Its services run on the
    // recording pipeline, so a request reaches the platform's binding and nothing behind it.

    /// <summary>Composes the projection suite's model.</summary>
    internal static class ProjectionModel
    {
        /// <summary>The center's resource.</summary>
        public const string Centers = "pj.PjCenter";

        /// <summary>The center note's resource, a child query surface.</summary>
        public const string Notes = "pj.PjCenterNote";

        /// <summary>The ledger's resource.</summary>
        public const string Ledgers = "pj.PjLedger";

        /// <summary>The currency's resource, a cacheable list.</summary>
        public const string Currencies = "pj.PjCurrency";

        /// <summary>The routed service's securable resource.</summary>
        public const string Inbox = "pj.Inbox";

        /// <summary>The confirmation keys the model declares.</summary>
        public static readonly string[] ConfirmationKeys = ["Pj.Backdated", "Pj.OpenOrders"];

        /// <summary>Adds the model to a composition.</summary>
        /// <param name="tellma">The composition.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            ArgumentNullException.ThrowIfNull(tellma);
            _ = tellma.AddFeature(Feature());
        }

        /// <summary>A feature contributing the model, then whatever else the scenario contributes.</summary>
        /// <param name="contribute">The rest of the scenario's contribution.</param>
        /// <returns>The feature.</returns>
        public static TestFeature<Tag.A> Feature(Action<FeatureContribution>? contribute = null)
        {
            return new TestFeature<Tag.A>("pj", contribute: contribution =>
            {
                _ = contribution
                    .Entity<PjCenter, PjCenterService>()
                    .Entity<PjLedger, PjLedgerService>()
                    .Entity<PjCurrency, PjCurrencyService>()
                    .ApiService<PjInboxService>()
                    .Confirmation(ConfirmationKeys[0])
                    .Confirmation(ConfirmationKeys[1])
                    .Securables(static builder => builder.MarkSensitive(Centers, "Transfer"));
                contribute?.Invoke(contribution);
            });
        }
    }

    /// <summary>A cost center: an activatable tree with notes.</summary>
    [Table("Centers", Schema = "pj")]
    [Tree(MaxDepth = 8)]
    [TableType]
    public class PjCenter : ActivatableTreeEntity
    {
        /// <summary>The code.</summary>
        [Column(TypeName = "varchar(50)")]
        [Unique]
        public string Code { get; set; } = null!;

        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The notes.</summary>
        [NotMapped]
        public List<PjCenterNote>? Notes { get; set; }
    }

    /// <summary>A note on a center: a child, queryable on its own surface.</summary>
    [Table("CenterNotes", Schema = "pj")]
    [TableType]
    public class PjCenterNote : ChildEntity
    {
        /// <summary>The center.</summary>
        [ParentKey]
        public int CenterId { get; set; }

        /// <summary>The text.</summary>
        [MaxLength(255)]
        public string? Text { get; set; }
    }

    /// <summary>A ledger: a stack keyed by <see cref="long" />, whose own save ceiling is below the web surface's.</summary>
    [Table("Ledgers", Schema = "pj")]
    [Stack(MaxSaveCount = LedgerMaxSaveCount)]
    [TableType]
    public class PjLedger : TopLevelEntity<long>
    {
        /// <summary>The most entities one ledger save carries.</summary>
        public const int LedgerMaxSaveCount = 2;

        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>A currency: a cacheable list.</summary>
    [Table("Currencies", Schema = "pj")]
    [Cacheable(MaxRows = 50)]
    [TableType]
    public class PjCurrency : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>The arguments of a center's repricing.</summary>
    /// <param name="Percent">The change, in percent.</param>
    public sealed record PjRepriceArguments(int Percent);

    /// <summary>The arguments of a center's scoring.</summary>
    /// <param name="Weight">The weight.</param>
    public sealed record PjScoreArguments(int Weight);

    /// <summary>The arguments of a center's transfer.</summary>
    /// <param name="ToParentId">The new parent.</param>
    public sealed record PjTransferArguments(int ToParentId);

    /// <summary>The body of a center's summary.</summary>
    /// <param name="Language">The language to summarise in.</param>
    public sealed record PjSummaryRequest(string Language);

    /// <summary>What the inbox summary answers.</summary>
    /// <param name="Unread">How many items are unread.</param>
    public sealed record PjInboxSummary(int Unread);

    /// <summary>The center's service: an entity action of every shape and two API actions.</summary>
    /// <param name="factory">The recording pipeline's factory.</param>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The signature is what the fixture declares; the recording pipeline answers in its place.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract; the registry refuses a static one.")]
    public sealed class PjCenterService(RecordingPipelineFactory factory) : EntityService<PjCenter>(factory)
    {
        /// <summary>Archives the centers: answers the entities, takes no arguments.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task.</returns>
        [EntityAction("archive")]
        public Task ArchiveAsync(ActionContext<PjCenter, int> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>Reprices the centers: answers the entities, takes arguments.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task.</returns>
        [EntityAction("reprice")]
        public Task RepriceAsync(ActionContext<PjCenter, int> context, PjRepriceArguments arguments)
        {
            return Task.CompletedTask;
        }

        /// <summary>Counts the centers' lines: its own result over many rows, no arguments.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The count.</returns>
        [EntityAction("count-lines", Mutation = false, Idempotent = true)]
        public Task<int> CountLinesAsync(ActionContext<PjCenter, int> context)
        {
            return Task.FromResult(0);
        }

        /// <summary>Scores the centers: its own result over many rows, with arguments.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The score.</returns>
        [EntityAction("score")]
        public Task<int> ScoreAsync(ActionContext<PjCenter, int> context, PjScoreArguments arguments)
        {
            return Task.FromResult(0);
        }

        /// <summary>Takes a center over: its own result over one row, no arguments.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The outcome.</returns>
        [EntityAction("take-over", SingleTarget = true)]
        public Task<string> TakeOverAsync(ActionContext<PjCenter, int> context)
        {
            return Task.FromResult(string.Empty);
        }

        /// <summary>Transfers a center: its own result over one row, with arguments, under a sensitive securable.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The outcome.</returns>
        [EntityAction("transfer", SingleTarget = true, AllowWhenReadOnly = true)]
        public Task<string> TransferAsync(ActionContext<PjCenter, int> context, PjTransferArguments arguments)
        {
            return Task.FromResult(string.Empty);
        }

        /// <summary>Rebuilds the centers in a job: a hand-off, under the long timeout.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The job.</returns>
        [EntityAction("rebuild", Long = true)]
        public Task<JobAccepted> RebuildAsync(ActionContext<PjCenter, int> context)
        {
            return Task.FromResult(new JobAccepted(0, null));
        }

        /// <summary>Summarises the centers: an API action with a body.</summary>
        /// <param name="request">The body.</param>
        /// <returns>The summary.</returns>
        [ApiAction("summary", Mutation = false, Idempotent = true, Action = "Read")]
        public Task<string> SummaryAsync(PjSummaryRequest request)
        {
            return Task.FromResult(string.Empty);
        }

        /// <summary>Pings: a member-only API action without a body or a result.</summary>
        /// <returns>A task.</returns>
        [ApiAction("ping", Mutation = false, MemberOnly = true)]
        public Task PingAsync()
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>The ledger's service, with nothing of its own.</summary>
    /// <param name="factory">The recording pipeline's factory.</param>
    public sealed class PjLedgerService(RecordingPipelineFactory factory) : EntityService<PjLedger, long>(factory);

    /// <summary>The currency's service, with nothing of its own.</summary>
    /// <param name="factory">The recording pipeline's factory.</param>
    public sealed class PjCurrencyService(RecordingPipelineFactory factory) : EntityService<PjCurrency>(factory);

    /// <summary>A routed service with a polling action.</summary>
    [ApiRoute("inbox")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract; the registry refuses a static one.")]
    public sealed class PjInboxService
    {
        /// <summary>Summarises the caller's inbox: polled, so it stamps no activity.</summary>
        /// <returns>The summary.</returns>
        [ApiAction("summary", Mutation = false, Idempotent = true, MemberOnly = true)]
        public Task<PjInboxSummary> SummaryAsync()
        {
            return Task.FromResult(new PjInboxSummary(0));
        }

        /// <summary>The caller's inbox document, serialized once by the service: written as its bytes.</summary>
        /// <returns>The document's UTF-8 JSON and its wire tag.</returns>
        [ApiAction("document", Mutation = false, Idempotent = true, MemberOnly = true)]
        public Task<(byte[] Json, string WireTag)> DocumentAsync()
        {
            return Task.FromResult((Array.Empty<byte>(), string.Empty));
        }

        /// <summary>Purges every inbox: a secured action.</summary>
        /// <returns>How many items were purged.</returns>
        [ApiAction("purge", Resource = ProjectionModel.Inbox, Destructive = true)]
        public Task<int> PurgeAsync()
        {
            return Task.FromResult(0);
        }
    }

    /// <summary>A cost center whose writable name never crosses the wire, which the contract audit refuses.</summary>
    [Table("HiddenCenters", Schema = "pj")]
    [TableType]
    public class PjHiddenCenter : TopLevelEntity
    {
        /// <summary>The name, ignored by JSON although the caller writes it.</summary>
        [MaxLength(255)]
        [JsonIgnore]
        public string? Name { get; set; }
    }

    /// <summary>A child whose surface takes an SPA page's route word, which the audit refuses.</summary>
    [Table("Reserved", Schema = "pj")]
    [TableType]
    [ApiResource(Segment = "settings")]
    public class PjReservedChild : ChildEntity
    {
        /// <summary>The ledger.</summary>
        [ParentKey]
        public long LedgerId { get; set; }
    }

    /// <summary>A ledger with a child whose surface takes a reserved word.</summary>
    [Table("ReservedLedgers", Schema = "pj")]
    [TableType]
    public class PjReservingLedger : TopLevelEntity<long>
    {
        /// <summary>The children.</summary>
        [NotMapped]
        public List<PjReservedChild>? Children { get; set; }
    }
}
