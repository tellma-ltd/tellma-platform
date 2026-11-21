// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     <c>jobs.handlers</c>: every entity-backed handler processes an entity the model maps as a
    ///     job entity, and a warning, never a problem, for each built-in schedule whose key no handler
    ///     on this host registers: a schedule for a handler another host runs is legal.
    /// </summary>
    /// <remarks>
    ///     A model that does not build is the entity-metadata check's to report; the entities are
    ///     then not judged.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class JobHandlersCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.JobHandlers;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];

            // --- Each entity-backed handler's entity, against the model ---
            if (services.GetService<EntityMetadataSource>()?.Build.Provider is IEntityMetadataProvider metadata)
            {
                HashSet<Type> mapped = [.. metadata.All.Where(static entity => entity.IsJobEntity).Select(static entity => entity.ClrType)];
                foreach (ContributedJobHandler handler in record.JobHandlers)
                {
                    if (handler.EntityType is Type entityType && !mapped.Contains(record.LeafOf(entityType)))
                    {
                        problems.Add(new CompositionProblem(
                            Name,
                            $"The job handler {TypeDisplay.Qualified(handler.HandlerType)} of the feature '{handler.Owner.Name}' processes {TypeDisplay.Qualified(entityType)}, which the model does not map as a job entity.",
                            "Contribute the entity (contribution.Entity<T>()) with a JobId column, or process an entity the model maps."));
                    }
                }
            }

            // --- A built-in whose key no handler here registers: a warning, never a problem ---
            ILogger logger = services.GetService<ILoggerFactory>()?.CreateLogger(JobsLog.Category) ?? NullLogger.Instance;
            HashSet<string> registered = new(record.JobHandlers.Select(static handler => handler.Attribute.Key), StringComparer.Ordinal);
            foreach (ContributedBuiltInSchedule schedule in record.BuiltInSchedules.Where(schedule => !registered.Contains(schedule.Seed.HandlerKey)))
            {
                JobsLog.SchedulerHandlerUnknown(logger, schedule.Seed.Id, schedule.Seed.HandlerKey);
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
