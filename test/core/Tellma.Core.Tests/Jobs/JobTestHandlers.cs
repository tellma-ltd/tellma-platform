// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>The job tests' model: two job entities, one keyed by a long and one by an int, and the sound handlers.</summary>
    internal static class JobTestModel
    {
        /// <summary>Adds the job tests' feature, named <c>acme</c>.</summary>
        /// <param name="tellma">The composition's builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.D>("acme", contribute: static contribution => Contribute(contribution)));
        }

        /// <summary>The job tests' entities and sound handlers.</summary>
        /// <param name="contribution">The contribution.</param>
        /// <returns>The contribution, for chaining.</returns>
        public static FeatureContribution Contribute(FeatureContribution contribution)
        {
            return contribution
                .Entity<Shipment>()
                .Entity<Inspection>()
                .JobHandler<RecomputeHandler>()
                .JobHandler<ShipHandler>()
                .JobHandler<InspectHandler>();
        }
    }

    // --- Job entities ---

    /// <summary>A job entity keyed by a long.</summary>
    [Table("Shipments", Schema = "jobs")]
    [TableType]
    public class Shipment : TopLevelEntity<long>, IJobEntity
    {
        /// <summary>The memo.</summary>
        [MaxLength(255)]
        public string? Memo { get; set; }

        /// <summary>The job processing the row.</summary>
        public int? JobId { get; set; }
    }

    /// <summary>A job entity keyed by an int.</summary>
    [Table("Inspections", Schema = "jobs")]
    [TableType]
    public class Inspection : TopLevelEntity, IJobEntity
    {
        /// <summary>The job processing the row.</summary>
        public int? JobId { get; set; }
    }

    // --- Sound handlers ---

    /// <summary>An arguments-only handler.</summary>
    [JobHandler("acme.recompute-balances", Schedulable = true)]
    public sealed class RecomputeHandler : IJobHandler
    {
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A second arguments-only handler.</summary>
    [JobHandler("acme.digest")]
    public sealed class DigestHandler : IJobHandler
    {
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>An entity-backed handler over the shipments, which are keyed by a long.</summary>
    [JobHandler("acme.ship")]
    public sealed class ShipHandler : IEntityJobHandler<Shipment>
    {
        public Task ExecuteAsync(JobBatch<Shipment> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>An entity-backed handler over the inspections, which are keyed by an int.</summary>
    [JobHandler("acme.inspect")]
    public sealed class InspectHandler : IEntityJobHandler<Inspection>
    {
        public Task ExecuteAsync(JobBatch<Inspection> batch)
        {
            return Task.CompletedTask;
        }
    }

    // --- Malformed handlers ---

    /// <summary>A handler with no attribute.</summary>
    public sealed class UnattributedHandler : IJobHandler
    {
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>An abstract handler.</summary>
    [JobHandler("acme.abstract")]
    public abstract class AbstractHandler : IJobHandler
    {
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A class with the attribute and neither handler interface.</summary>
    [JobHandler("acme.shapeless")]
    public sealed class ShapelessHandler;

    /// <summary>A handler implementing both shapes.</summary>
    [JobHandler("acme.both")]
    public sealed class BothShapesHandler : IJobHandler, IEntityJobHandler<Shipment>
    {
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }

        public Task ExecuteAsync(JobBatch<Shipment> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A handler whose key breaks the grammar.</summary>
    [JobHandler("Acme_Bad")]
    public sealed class MalformedKeyHandler : IJobHandler
    {
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A handler whose batch size is out of range.</summary>
    [JobHandler("acme.oversized", BatchSize = 501)]
    public sealed class OversizedHandler : IJobHandler
    {
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A handler that claims the platform's prefix.</summary>
    [JobHandler("core.impostor")]
    public sealed class PlatformKeyHandler : IJobHandler
    {
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A second handler registering <see cref="RecomputeHandler" />'s key.</summary>
    [JobHandler("acme.recompute-balances")]
    public sealed class DuplicateRecomputeHandler : IJobHandler
    {
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>An entity-backed handler over an entity the model does not map.</summary>
    [JobHandler("acme.unmapped")]
    public sealed class UnmappedEntityHandler : IEntityJobHandler<UnmappedJobEntity>
    {
        public Task ExecuteAsync(JobBatch<UnmappedJobEntity> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A job entity no feature contributes.</summary>
    public sealed class UnmappedJobEntity : Entity<int>, IJobEntity
    {
        public int? JobId { get; set; }
    }
}
