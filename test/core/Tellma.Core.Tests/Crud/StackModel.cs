// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud
{
    // The stack suite's model: a gadget aggregate two levels deep with a leaf a distribution
    // substitutes, and two plain entities an enlistment targets. Every service class in this
    // assembly is written for the gadget, and every composition that stacks the gadget names its
    // service, so the check for service classes no stack runs judges only the scenario built for it.

    /// <summary>Composes the stack suite's model.</summary>
    internal static class StackModel
    {
        /// <summary>The gadget's resource.</summary>
        public const string Gadgets = "sm.SmGadget";

        /// <summary>The gadget part's resource.</summary>
        public const string Parts = "sm.SmGadgetPart";

        /// <summary>The gadget part note's resource.</summary>
        public const string Notes = "sm.SmGadgetPartNote";

        /// <summary>The gizmo's resource.</summary>
        public const string Gizmos = "sm.SmGizmo";

        /// <summary>The sprocket's resource.</summary>
        public const string Sprockets = "sm.SmSprocket";

        /// <summary>A feature stacking the gadget with a service, then whatever else the scenario contributes.</summary>
        /// <typeparam name="TService">The gadget's service.</typeparam>
        /// <param name="contribute">The rest of the scenario's contribution.</param>
        /// <param name="name">The feature's name.</param>
        /// <returns>The feature.</returns>
        public static TestFeature<Tag.D> Feature<TService>(Action<FeatureContribution>? contribute = null, string name = "sm")
            where TService : class
        {
            return new TestFeature<Tag.D>(name, contribute: contribution =>
            {
                _ = contribution.Entity<SmGadget, TService>();
                contribute?.Invoke(contribution);
            });
        }
    }

    /// <summary>A gadget: an activatable, multilingual aggregate root with parts.</summary>
    [Table("Gadgets", Schema = "sm")]
    [TableType]
    public class SmGadget : TopLevelEntity, IActivatable
    {
        /// <summary>The code.</summary>
        [Column(TypeName = "varchar(50)")]
        [Unique]
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

        /// <summary>The gizmo the gadget is built on.</summary>
        [References(typeof(SmGizmo))]
        public int? GizmoId { get; set; }

        /// <summary>Whether the gadget is active.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>The parts.</summary>
        [NotMapped]
        public List<SmGadgetPart>? Parts { get; set; }
    }

    /// <summary>A distribution's gadget, substituting the pack's.</summary>
    public class SmMyGadget : SmGadget
    {
        /// <summary>The colour.</summary>
        [MaxLength(10)]
        public string? Colour { get; set; }
    }

    /// <summary>A gadget's part, with notes of its own.</summary>
    [Table("GadgetParts", Schema = "sm")]
    [TableType]
    public class SmGadgetPart : ChildEntity
    {
        /// <summary>The gadget.</summary>
        [ParentKey]
        public int GadgetId { get; set; }

        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string? Name { get; set; }

        /// <summary>The gizmo the part is.</summary>
        [References(typeof(SmGizmo))]
        public int? GizmoId { get; set; }

        /// <summary>The notes.</summary>
        [NotMapped]
        public List<SmGadgetPartNote>? Notes { get; set; }
    }

    /// <summary>A note on a gadget's part.</summary>
    [Table("GadgetPartNotes", Schema = "sm")]
    [TableType]
    [ApiResource(Segment = "part-notes")]
    public class SmGadgetPartNote : ChildEntity
    {
        /// <summary>The part.</summary>
        [ParentKey]
        public int GadgetPartId { get; set; }

        /// <summary>The text.</summary>
        [MaxLength(255)]
        public string? Text { get; set; }
    }

    /// <summary>A gizmo: a plain entity that accepts enlisted writes.</summary>
    [Table("Gizmos", Schema = "sm")]
    [TableType]
    [Stack(Operations = StackOperations.All | StackOperations.Enlist)]
    public class SmGizmo : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>A sprocket: a plain entity that accepts enlisted writes.</summary>
    [Table("Sprockets", Schema = "sm")]
    [TableType]
    [Stack(Operations = StackOperations.All | StackOperations.Enlist)]
    public class SmSprocket : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>A distribution's role leaf that serves every operation, the delete by query among them, which the startup gate refuses.</summary>
    [Stack(Operations = StackOperations.All)]
    public sealed class SmEveryRole : Role
    {
    }

    /// <summary>The arguments of the gadget's repricing.</summary>
    /// <param name="Price">The new price.</param>
    /// <param name="GizmoId">The gizmo the price follows.</param>
    public sealed record RepriceArguments(decimal Price, [property: EntityRef(StackModel.Gizmos)] int? GizmoId);

    /// <summary>The body of the gadget's summary.</summary>
    /// <param name="Language">The language to summarise in.</param>
    public sealed record SummaryRequest(string Language);

    /// <summary>The gadget's service: an entity action of each result shape and an API action.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    /// <param name="factory">The pipeline factory.</param>
    [DetailsExtras("history")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The signature is what the fixture declares; the platform reads it, never runs it.")]
    public class GadgetService<TGadget>(IEntityPipelineFactory factory) : EntityService<TGadget>(factory)
        where TGadget : SmGadget
    {
        /// <summary>Archives the gadgets.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the gadgets are archived.</returns>
        [EntityAction("archive", Idempotent = true, Description = "Archives the gadgets.")]
        public virtual Task ArchiveAsync(ActionContext<TGadget, int> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>Reprices the gadgets.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The new price.</param>
        /// <returns>How many gadgets were repriced.</returns>
        [EntityAction("prices/reprice", Long = true, SupportsFilter = false, LoadChildren = true)]
        public Task<int> RepriceAsync(ActionContext<TGadget, int> context, RepriceArguments arguments)
        {
            return Task.FromResult(0);
        }

        /// <summary>Summarises the gadgets.</summary>
        /// <param name="request">The body.</param>
        /// <param name="cancellationToken">Cancels the summary.</param>
        /// <returns>The summary.</returns>
        [ApiAction("summary", Mutation = false, Idempotent = true, Action = "Read")]
        public Task<string> SummaryAsync(SummaryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(request.Language);
        }
    }

    /// <summary>The gadget's service with one more action, declared after its base's.</summary>
    /// <param name="factory">The pipeline factory.</param>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The signature is what the fixture declares; the platform reads it, never runs it.")]
    public sealed class ExtendedGadgetService(IEntityPipelineFactory factory) : GadgetService<SmGadget>(factory)
    {
        /// <summary>Archives the gadgets, as the base does.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the gadgets are archived.</returns>
        public override Task ArchiveAsync(ActionContext<SmGadget, int> context)
        {
            return base.ArchiveAsync(context);
        }

        /// <summary>Takes the gadgets over.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The single gadget taken over.</returns>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract; the registry refuses a static one.")]
        [EntityAction("take-over", SingleTarget = true, Destructive = true, Mcp = McpExposure.Hidden)]
        public Task<string> TakeOverAsync(ActionContext<SmGadget, int> context)
        {
            return Task.FromResult(string.Empty);
        }
    }

    /// <summary>A stack companion of the gadget, generic over its leaf.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    public sealed class GadgetOperations<TGadget>
        where TGadget : SmGadget
    {
        /// <summary>Exports the gadgets: a segment the stack does not project when it omits Export.</summary>
        /// <returns>A task.</returns>
        [ApiAction("export", Mutation = false)]
        public Task ExportAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>Counts the gadgets.</summary>
        /// <returns>The count.</returns>
        [ApiAction("count", Mutation = false, MemberOnly = true)]
        public Task<int> CountAsync()
        {
            return Task.FromResult(0);
        }
    }

    /// <summary>A validator generic over the gadget, which follows any leaf.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    public sealed class GadgetRules<TGadget> : IEntityValidator<TGadget>
        where TGadget : SmGadget
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<TGadget> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A validator only the distribution's leaf satisfies.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    public sealed class MyGadgetRules<TGadget> : IEntityValidator<TGadget>
        where TGadget : SmMyGadget
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<TGadget> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A validator written for the pack's gadget alone.</summary>
    public sealed class PlainGadgetRules : IEntityValidator<SmGadget>
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<SmGadget> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A details contributor of the gadget, with one extra.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    [DetailsExtras("lineage")]
    public sealed class GadgetDetails<TGadget> : IDetailsContributor<TGadget>
        where TGadget : SmGadget
    {
        /// <inheritdoc />
        public void Contribute(DetailsPlan<TGadget> plan, IReadOnlySet<string> requested)
        {
        }
    }

    /// <summary>The gadget's named filters, built once without a tenant scope.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    public sealed class GadgetFilters<TGadget> : INamedFilterContributor<TGadget>
        where TGadget : SmGadget
    {
        /// <summary>Creates the contributor, counting it when the provider carries a counter.</summary>
        /// <param name="count">The counter, which only the test that counts registers.</param>
        public GadgetFilters(GadgetFilterCount? count = null)
        {
            count?.Increment();
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, FilterTree> NamedFilters { get; } = new Dictionary<string, FilterTree>
        {
            ["active"] = FilterTree.Leaf("IsActive"),
            ["on-gizmo"] = FilterTree.Via("Gizmo", FilterTree.Leaf("Name != ''")),
            ["with-parts"] = FilterTree.Exists(StackModel.Parts, "Gadget", FilterTree.Leaf("Name != ''")),
        };
    }

    /// <summary>
    ///     Counts the gadget filter contributors one provider creates, so a test sees each built
    ///     once per registry whatever other tests run alongside it.
    /// </summary>
    public sealed class GadgetFilterCount
    {
        private int _created;

        /// <summary>How many were created.</summary>
        public int Created => Volatile.Read(ref _created);

        /// <summary>Counts one more.</summary>
        public void Increment()
        {
            _ = Interlocked.Increment(ref _created);
        }
    }

    /// <summary>A persist effect of the gizmo that enlists sprockets.</summary>
    public sealed class GizmoEffect : IPersistEffect<SmGizmo>, IEnlists<SmSprocket>
    {
        /// <inheritdoc />
        public Task ContributeAsync(PersistContext<SmGizmo> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A persist effect of the sprocket that enlists gizmos, closing a cycle with the gizmo's.</summary>
    public sealed class SprocketEffect : IPersistEffect<SmSprocket>, IEnlists<SmGizmo>
    {
        /// <inheritdoc />
        public Task ContributeAsync(PersistContext<SmSprocket> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A latch: a plain entity serving every operation, whose deletes a service or a validator may judge.</summary>
    [Table("Latches", Schema = "sm")]
    [TableType]
    [Stack(Operations = StackOperations.All)]
    public class SmLatch : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>A latch service that judges deletes, which a stack serving a delete by query may not have.</summary>
    /// <param name="factory">The pipeline factory.</param>
    public sealed class JudgingLatchService(IEntityPipelineFactory factory) : EntityService<SmLatch>(factory)
    {
        /// <inheritdoc />
        protected override Task ValidateDeleteAsync(DeleteContext<SmLatch, int> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A latch service that judges nothing, which the validator scenarios name so the judging
    ///     service is never left without a stack to run it.
    /// </summary>
    /// <param name="factory">The pipeline factory.</param>
    public sealed class PlainLatchService(IEntityPipelineFactory factory) : EntityService<SmLatch>(factory);

    /// <summary>A latch validator that judges deletes, which a stack serving a delete by query may not have.</summary>
    public sealed class LatchDeleteRules : IEntityValidator<SmLatch>
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<SmLatch> context)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ValidateDeleteAsync(DeleteContext<SmLatch> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A latch validator that judges saves alone, which leaves a delete by query nothing to skip.</summary>
    public sealed class LatchSaveRules : IEntityValidator<SmLatch>
    {
        /// <inheritdoc />
        public Task ValidateAsync(SaveContext<SmLatch> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>The gadget's service writing its own entity from an open frame: a self-pair.</summary>
    /// <param name="factory">The pipeline factory.</param>
    public sealed class SelfEnlistingGadgetService(IEntityPipelineFactory factory) : EntityService<SmGadget>(factory), IEnlists<SmGadget>, IEnlists<SmGizmo>;

    /// <summary>A routed service with a member-only and a secured action.</summary>
    [ApiRoute("sm-inbox")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The signature is what the fixture declares; the platform reads it, never runs it.")]
    public sealed class InboxService
    {
        /// <summary>Marks the caller's inbox read.</summary>
        /// <returns>A task.</returns>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract; the registry refuses a static one.")]
        [ApiAction("mark-read", MemberOnly = true)]
        public Task MarkReadAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>Purges every inbox.</summary>
        /// <param name="cancellationToken">Cancels the purge.</param>
        /// <returns>How many items were purged.</returns>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract; the registry refuses a static one.")]
        [ApiAction("purge", Resource = "sm.Inbox", Destructive = true)]
        public Task<int> PurgeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
