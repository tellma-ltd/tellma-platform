// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Crud
{
    // The startup checks' failing fixtures: one class or entity per rule a check enforces, each
    // composed by the scenario that expects its problem. Every service class is written for the
    // gadget (see StackModel.cs).

    /// <summary>An entity whose stack declares a ceiling of zero.</summary>
    [Table("Ceilings", Schema = "sm")]
    [TableType]
    [Stack(MaxTake = 0)]
    public class SmCeiling : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>An entity whose segment collides with the gadget's.</summary>
    [Table("Clashes", Schema = "sm")]
    [TableType]
    [ApiResource(Segment = "gadgets")]
    public class SmClash : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>An entity whose segment is not kebab-case.</summary>
    [Table("Oddities", Schema = "sm")]
    [TableType]
    [ApiResource(Segment = "Odd_Segment")]
    public class SmOddity : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>An entity whose projection texts do not validate.</summary>
    [Table("Projections", Schema = "sm")]
    [TableType]
    [DefaultSelect("Id, Nope")]
    [DetailsExpand("CreatedBy", "Nowhere.Else")]
    [Stack(DefaultOrderBy = "Missing desc")]
    public class SmProjection : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>A distribution's gadget with a collection whose child type opts out of a query surface.</summary>
    public sealed class SmQuietGadget : SmGadget
    {
        /// <summary>The quiet parts.</summary>
        [NotMapped]
        public List<SmQuietPart>? QuietParts { get; set; }
    }

    /// <summary>A child type that opts out of a query surface, with a child of its own.</summary>
    [Table("QuietParts", Schema = "sm")]
    [TableType]
    [ApiResource(Queryable = false)]
    public class SmQuietPart : ChildEntity
    {
        /// <summary>The gadget.</summary>
        [ParentKey]
        public int GadgetId { get; set; }

        /// <summary>The notes.</summary>
        [NotMapped]
        public List<SmQuietNote>? Notes { get; set; }
    }

    /// <summary>A note on a quiet part, whose owner has no surface.</summary>
    [Table("QuietNotes", Schema = "sm")]
    [TableType]
    public class SmQuietNote : ChildEntity
    {
        /// <summary>The part.</summary>
        [ParentKey]
        public int QuietPartId { get; set; }
    }

    /// <summary>Arguments that are not a record.</summary>
    public sealed class NotARecord
    {
        /// <summary>A value.</summary>
        public int Value { get; set; }
    }

    /// <summary>Arguments referencing a resource no stack owns.</summary>
    /// <param name="TargetId">The target.</param>
    public sealed record DanglingArguments([property: EntityRef("sm.Nothing")] int TargetId);

    /// <summary>A gadget service whose every action breaks a rule of its own.</summary>
    /// <param name="factory">The pipeline factory.</param>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The fixture's actions are instance methods by contract.")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The signature is what the fixture declares; the platform reads it, never runs it.")]
    public sealed class MisdeclaredGadgetService(IEntityPipelineFactory factory) : EntityService<SmGadget>(factory)
    {
        /// <summary>A name outside the grammar.</summary>
        /// <param name="context">The context.</param>
        /// <returns>A task.</returns>
        [EntityAction("Bad Name")]
        public Task BadNameAsync(ActionContext<SmGadget, int> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>A standard segment the stack projects.</summary>
        /// <param name="context">The context.</param>
        /// <returns>A task.</returns>
        [EntityAction("save")]
        public Task SaveAgainAsync(ActionContext<SmGadget, int> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>The first of two actions with one name.</summary>
        /// <param name="context">The context.</param>
        /// <returns>A task.</returns>
        [EntityAction("twice")]
        public Task FirstAsync(ActionContext<SmGadget, int> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>The second of two actions with one name.</summary>
        /// <param name="context">The context.</param>
        /// <returns>A task.</returns>
        [EntityAction("twice")]
        public Task SecondAsync(ActionContext<SmGadget, int> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>A signature that takes no action context.</summary>
        /// <param name="value">A value.</param>
        /// <returns>A task.</returns>
        [EntityAction("no-context")]
        public Task NoContextAsync(int value)
        {
            return Task.CompletedTask;
        }

        /// <summary>Arguments that are not a record.</summary>
        /// <param name="context">The context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task.</returns>
        [EntityAction("class-arguments")]
        public Task ClassArgumentsAsync(ActionContext<SmGadget, int> context, NotARecord arguments)
        {
            return Task.CompletedTask;
        }

        /// <summary>A single-target action that answers nothing.</summary>
        /// <param name="context">The context.</param>
        /// <returns>A task.</returns>
        [EntityAction("single", SingleTarget = true)]
        public Task SingleAsync(ActionContext<SmGadget, int> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>Arguments naming a resource no stack owns.</summary>
        /// <param name="context">The context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task.</returns>
        [EntityAction("dangling")]
        public Task DanglingAsync(ActionContext<SmGadget, int> context, DanglingArguments arguments)
        {
            return Task.CompletedTask;
        }

        /// <summary>Two bodies.</summary>
        /// <param name="first">The first.</param>
        /// <param name="second">The second.</param>
        /// <returns>A task.</returns>
        [ApiAction("two-bodies")]
        public Task TwoBodiesAsync(SummaryRequest first, SummaryRequest second)
        {
            return Task.CompletedTask;
        }

        /// <summary>A resource of its own on a stack's service.</summary>
        /// <returns>A task.</returns>
        [ApiAction("elsewhere", Resource = "sm.Elsewhere")]
        public Task ElsewhereAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>Both kinds at once.</summary>
        /// <param name="context">The context.</param>
        /// <returns>A task.</returns>
        [EntityAction("both")]
        [ApiAction("both")]
        public Task BothAsync(ActionContext<SmGadget, int> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A gadget service with an API action named <c>export</c>.</summary>
    /// <param name="factory">The pipeline factory.</param>
    public sealed class ExportingGadgetService(IEntityPipelineFactory factory) : EntityService<SmGadget>(factory)
    {
        /// <summary>Exports the gadgets its own way.</summary>
        /// <returns>A task.</returns>
        [ApiAction("export", Mutation = false)]
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract.")]
        public Task ExportAsync()
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A gadget service that declares the platform's own extra.</summary>
    /// <param name="factory">The pipeline factory.</param>
    [DetailsExtras("permissions")]
    public sealed class PermissionsGadgetService(IEntityPipelineFactory factory) : EntityService<SmGadget>(factory);

    /// <summary>A companion that declares an entity action and collides with the service's action.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The fixture's actions are instance methods by contract.")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The signature is what the fixture declares; the platform reads it, never runs it.")]
    public sealed class MisdeclaredOperations<TGadget>
        where TGadget : SmGadget
    {
        /// <summary>An entity action on a companion.</summary>
        /// <param name="context">The context.</param>
        /// <returns>A task.</returns>
        [EntityAction("stray")]
        public Task StrayAsync(ActionContext<TGadget, int> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>The service's action's name.</summary>
        /// <returns>A task.</returns>
        [ApiAction("summary")]
        public Task SummaryAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>A standard segment the stack projects.</summary>
        /// <returns>A task.</returns>
        [ApiAction("query")]
        public Task QueryAsync()
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A companion declaring no action.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    public sealed class IdleOperations<TGadget>
        where TGadget : SmGadget;

    /// <summary>A routed service with no route.</summary>
    public sealed class UnroutedService;

    /// <summary>A routed service whose actions name no securable, or declare an entity action.</summary>
    [ApiRoute("reports")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The fixture's actions are instance methods by contract.")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The signature is what the fixture declares; the platform reads it, never runs it.")]
    public sealed class UnsecuredService
    {
        /// <summary>No resource and not member-only.</summary>
        /// <returns>A task.</returns>
        [ApiAction("run")]
        public Task RunAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>An entity action on a routed service.</summary>
        /// <param name="context">The context.</param>
        /// <returns>A task.</returns>
        [EntityAction("stray")]
        public Task StrayAsync(ActionContext<SmGadget, int> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A details contributor declaring one extra twice, once through its base.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    [DetailsExtras("twin")]
    public sealed class TwinDetails<TGadget> : TwinDetailsBase<TGadget>
        where TGadget : SmGadget;

    /// <summary>The base of <see cref="TwinDetails{TGadget}" />.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    [DetailsExtras("twin")]
    public class TwinDetailsBase<TGadget> : IDetailsContributor<TGadget>
        where TGadget : SmGadget
    {
        /// <inheritdoc />
        public void Contribute(DetailsPlan<TGadget> plan, IReadOnlySet<string> requested)
        {
        }
    }

    /// <summary>Named filters breaking the name grammar and the schema, and repeating the gadget filters' name.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    public sealed class BrokenFilters<TGadget> : INamedFilterContributor<TGadget>
        where TGadget : SmGadget
    {
        /// <inheritdoc />
        public IReadOnlyDictionary<string, FilterTree> NamedFilters { get; } = new Dictionary<string, FilterTree>
        {
            ["Mine"] = FilterTree.Leaf("IsActive"),
            ["active"] = FilterTree.Leaf("IsActive"),
            ["unknown"] = FilterTree.Leaf("Nope = 1"),
            ["nowhere"] = FilterTree.Via("Nowhere", FilterTree.Leaf("IsActive")),
            ["sideways"] = FilterTree.Exists(StackModel.Parts, "Gizmo", FilterTree.Leaf("Name != ''")),
        };
    }

    /// <summary>A named filter contributor whose constructor fails.</summary>
    /// <typeparam name="TGadget">The gadget leaf.</typeparam>
    public sealed class ThrowingFilters<TGadget> : INamedFilterContributor<TGadget>
        where TGadget : SmGadget
    {
        /// <summary>Fails.</summary>
        /// <exception cref="InvalidOperationException">Always.</exception>
        public ThrowingFilters()
        {
            throw new InvalidOperationException("no tenant here");
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, FilterTree> NamedFilters => throw new InvalidOperationException("Unreachable.");
    }

    /// <summary>A pipeline factory a host substitutes, which the platform refuses.</summary>
    public sealed class SubstitutePipelineFactory : IEntityPipelineFactory
    {
        /// <inheritdoc />
        public IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
            where TEntity : Entity<TKey>
            where TKey : struct, System.Numerics.IBinaryInteger<TKey>
        {
            throw new NotSupportedException("A substitute is refused at startup.");
        }
    }
}
