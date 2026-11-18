// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Numerics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Crud.Details;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Ids;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Crud.Save
{
    // The save is spec 0015 §6: §6.1 steps 1 to 6 (the shape, the type-level grant, preprocessing,
    // the ids, round trip 1 and the ordered validation of §6.6), then steps 7 to 10 (the persist
    // batch of §6.7, its round trip, the read-back of §6.9 and the post-commit phase of §13.2).
    /// <summary>
    ///     The save of a stack: shapes, preprocesses and validates a payload, assigns its new rows'
    ///     ids, loads what the validation needs in shared rounds, then persists the payload in one
    ///     batch and runs the post-commit phase. Scoped.
    /// </summary>
    /// <param name="evaluator">The caller's access decisions.</param>
    /// <param name="securables">The registered securables, which the decisions on references are taken over.</param>
    /// <param name="runner">The guarded batch runner.</param>
    /// <param name="operations">The operation scope.</param>
    /// <param name="metrics">The pipeline's instruments.</param>
    /// <param name="walker">The attribute walker of the payload's first check.</param>
    /// <param name="confirmations">The scope's confirmations, which each save opens a frame on.</param>
    /// <param name="metadata">The tenant model's entity metadata.</param>
    /// <param name="schemas">The tenant schemas and their maps.</param>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="data">The data layer's options, whose large-batch threshold picks a save's lane.</param>
    /// <param name="allocator">The id allocator, whose reservation rides round trip 1 when the buffer falls short.</param>
    /// <param name="buffer">The id allocator's buffer, which serves a reservation before any batch exists.</param>
    /// <param name="database">The scope's tenant database, whose location picks the buffers that reservation is served from.</param>
    /// <param name="valueSets">The value sets the payload's values are checked against.</param>
    /// <param name="services">The scope's services, which hold each entity's validators.</param>
    /// <param name="stepUp">The assurance bar of the sensitive securables, which a reference to a sensitive target is held to.</param>
    /// <param name="time">The clock the access decisions are taken at.</param>
    /// <param name="details">The details planner, which composes the read-back.</param>
    /// <param name="guards">The access guards of the security tables.</param>
    /// <param name="frames">The write frames the scope's persists may be nested in.</param>
    /// <param name="logger">The log of the post-commit failures.</param>
    internal sealed class SaveOperation(
        AccessEvaluator evaluator,
        ISecurableRegistry securables,
        IGuardedBatchRunner runner,
        CrudOperations operations,
        CrudMetrics metrics,
        AttributeWalker walker,
        Confirmations confirmations,
        IEntityMetadataProvider metadata,
        QueryexSchemaProvider schemas,
        IRequestContextAccessor accessor,
        IOptions<DataOptions> data,
        IIdAllocator allocator,
        IIdBuffer buffer,
        ITenantDatabase database,
        IValueSetRegistry valueSets,
        IServiceProvider services,
        StepUpBar stepUp,
        TimeProvider time,
        DetailsPlanner details,
        IAccessGuards guards,
        WriteFrames frames,
        ILogger<SaveOperation> logger)
    {
        /// <summary>The dependency a data-access failure names.</summary>
        private const string DatabaseDependency = "database";

        /// <summary>The wait a caller is told before retrying a write the database could not serve.</summary>
        private static readonly TimeSpan DatabaseRetryAfter = TimeSpan.FromSeconds(1);

        /// <summary>The owner of each child entity, by the child's name; built on first use.</summary>
        private Dictionary<string, EntityOwner>? _owners;

        /// <summary>The caller's access decisions.</summary>
        internal AccessEvaluator Evaluator { get; } = evaluator;

        /// <summary>The registered securables.</summary>
        internal ISecurableRegistry Securables { get; } = securables;

        /// <summary>The guarded batch runner.</summary>
        internal IGuardedBatchRunner Runner { get; } = runner;

        /// <summary>The pipeline's instruments.</summary>
        internal CrudMetrics Metrics { get; } = metrics;

        /// <summary>The attribute walker.</summary>
        internal AttributeWalker Walker { get; } = walker;

        /// <summary>The scope's confirmations.</summary>
        internal Confirmations Confirmations { get; } = confirmations;

        /// <summary>The tenant model's entity metadata.</summary>
        internal IEntityMetadataProvider Metadata { get; } = metadata;

        /// <summary>The tenant schemas and their maps.</summary>
        internal QueryexSchemaProvider Schemas { get; } = schemas;

        /// <summary>The scope's request context.</summary>
        internal IRequestContextAccessor Accessor { get; } = accessor;

        /// <summary>The id allocator.</summary>
        internal IIdAllocator Allocator { get; } = allocator;

        /// <summary>The id allocator's buffer.</summary>
        internal IIdBuffer Buffer { get; } = buffer;

        /// <summary>The scope's tenant database.</summary>
        internal ITenantDatabase Database { get; } = database;

        /// <summary>The value sets.</summary>
        internal IValueSetRegistry ValueSets { get; } = valueSets;

        /// <summary>The scope's services.</summary>
        internal IServiceProvider Services { get; } = services;

        /// <summary>The assurance bar of the sensitive securables.</summary>
        internal StepUpBar StepUp { get; } = stepUp;

        /// <summary>The clock.</summary>
        internal TimeProvider Time { get; } = time;

        /// <summary>The details planner.</summary>
        internal DetailsPlanner Details { get; } = details;

        /// <summary>The access guards of the security tables.</summary>
        internal IAccessGuards AccessGuards { get; } = guards;

        /// <summary>The write frames the scope's persists may be nested in.</summary>
        internal WriteFrames Frames { get; } = frames;

        /// <summary>The log of the post-commit failures.</summary>
        internal ILogger Logger { get; } = logger;

        /// <summary>The data layer's large-batch threshold, which picks a save's lane.</summary>
        internal int LargeBatchThreshold => data.Value.LargeBatchThreshold;

        /// <summary>The entity whose child collection holds a child entity, and the child's navigation to that entity's row.</summary>
        /// <param name="child">The child entity.</param>
        /// <returns>The owner; null for an entity no collection holds.</returns>
        internal EntityOwner? OwnerOf(EntityMetadata child)
        {
            ArgumentNullException.ThrowIfNull(child);
            _owners ??= Owners(Metadata);
            return _owners.GetValueOrDefault(child.Name);
        }

        /// <summary>Saves a payload of a stack's rows: inserts the new ones, updates the others, synchronises their children.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="entities">The payload, which the save shapes in place.</param>
        /// <param name="options">The save's options.</param>
        /// <param name="cancellationToken">Cancels the save.</param>
        /// <returns>The saved rows' ids, and the rows when asked for.</returns>
        public Task<EntitiesResult<TEntity>> SaveAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(options);

            // The lane counts the payload's rows at every depth; a null row is refused by the first check.
            EntityMetadata root = Metadata.Get(typeof(TEntity));
            string lane = CrudMetrics.Lane(CountRows(root, entities), data.Value.LargeBatchThreshold);
            return operations.RunAsync(
                stack.Resource,
                CrudOperationNames.Save,
                CrudMetrics.Source(options.Source),
                lane,
                () => TranslatedAsync(() => new SaveRun<TEntity, TKey>(this, stack, behavior, entities, options, cancellationToken).RunAsync()),
                cancellationToken);
        }

        /// <summary>Runs a write's steps with an exhausted retry translated into what the caller can act on.</summary>
        /// <typeparam name="TResult">What the write returns.</typeparam>
        /// <param name="steps">The write's steps.</param>
        /// <returns>What the write returned.</returns>
        internal static async Task<TResult> TranslatedAsync<TResult>(Func<Task<TResult>> steps)
        {
            try
            {
                return await steps().ConfigureAwait(false);
            }
            catch (DataAccessRetryExhaustedException failure)
            {
                // Deadlocks and transient failures outlasted the executor's retries: worth a retry soon.
                throw new DependencyUnavailableException(DatabaseDependency, DatabaseRetryAfter, failure);
            }
        }

        /// <summary>Maps every child entity to the entity whose collection holds it.</summary>
        /// <param name="metadata">The tenant model's entity metadata.</param>
        /// <returns>The owners, by the child's name.</returns>
        private static Dictionary<string, EntityOwner> Owners(IEntityMetadataProvider metadata)
        {
            Dictionary<string, EntityOwner> owners = new(StringComparer.Ordinal);
            foreach (EntityMetadata entity in metadata.All)
            {
                foreach (ChildCollectionMetadata collection in entity.Children)
                {
                    // A child's one parent key names one owner, whichever of the owner's collections holds it.
                    string? navigation = collection.Child.References.FirstOrDefault(reference => reference.ForeignKey.Name == collection.ParentKey.Name)?.Navigation;
                    _ = owners.TryAdd(collection.Child.Name, new EntityOwner(entity, collection.ParentKey, navigation));
                }
            }

            return owners;
        }

        /// <summary>Counts a payload's rows at every depth, skipping a null row, which the first check refuses.</summary>
        /// <param name="entity">The rows' entity.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The count.</returns>
        private static int CountRows(EntityMetadata entity, IEnumerable<object?> rows)
        {
            int count = 0;
            foreach (object? row in rows)
            {
                if (row is null)
                {
                    continue;
                }

                count++;
                foreach (ChildCollectionMetadata collection in entity.Children)
                {
                    if (collection.Getter(row) is IReadOnlyList<object> children)
                    {
                        count += CountRows(collection.Child, children);
                    }
                }
            }

            return count;
        }
    }

    /// <summary>The entity whose child collection holds a child entity, and how the child reaches that entity's row.</summary>
    /// <param name="Entity">The owning entity.</param>
    /// <param name="ParentKey">The child's parent key, which references the owner's row.</param>
    /// <param name="Navigation">The child's navigation through its parent key; null when the child declares none.</param>
    internal sealed record EntityOwner(EntityMetadata Entity, PropertyMetadata ParentKey, string? Navigation);
}
