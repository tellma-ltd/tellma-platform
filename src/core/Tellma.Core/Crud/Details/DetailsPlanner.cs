// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Numerics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Details
{
    // The plan is spec 0015 §5.3; the same plan serves the save's read-back over the saved ids (§6.9)
    // and an import's hydration (spec 0019, DetailsRequest.None).
    /// <summary>
    ///     Composes a stack's details plan on a batch under one connected user: the main ids under
    ///     the caller's row filter, the rows with every collection and the related projection, the
    ///     extras its producers add, and the reads behind the record permissions. Scoped: the
    ///     details contributors and the access decisions are the scope's.
    /// </summary>
    /// <param name="evaluator">The caller's access decisions.</param>
    /// <param name="securables">The registered securables.</param>
    /// <param name="services">The scope's services, which hold the details contributors.</param>
    /// <param name="projections">The details projections.</param>
    /// <param name="schemas">The tenant schemas and their maps.</param>
    /// <param name="materializer">The entity materializer.</param>
    /// <param name="metadata">The tenant model's entity metadata.</param>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="time">The clock.</param>
    internal sealed class DetailsPlanner(
        AccessEvaluator evaluator,
        ISecurableRegistry securables,
        IServiceProvider services,
        DetailsProjections projections,
        QueryexSchemaProvider schemas,
        EntityMaterializer materializer,
        IEntityMetadataProvider metadata,
        IRequestContextAccessor accessor,
        TimeProvider time)
    {
        /// <summary>The problem code of a refused securable.</summary>
        private const string ForbiddenCode = "forbidden";

        /// <summary>
        ///     Refuses a request's extras before any round trip: more names than the stack's ceiling,
        ///     or a name the stack does not serve.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <param name="request">The request.</param>
        /// <exception cref="LimitExceededException">More extras than the stack's ceiling.</exception>
        /// <exception cref="BadRequestException">An extra the stack does not serve.</exception>
        public static void Admit(StackDescriptor stack, DetailsRequest request)
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(request);
            string[] names = [.. request.Include.Distinct(StringComparer.Ordinal)];
            if (names.Length > stack.Limits.MaxExtras)
            {
                throw new LimitExceededException(LimitNames.MaxExtras, names.Length, stack.Limits.MaxExtras);
            }

            if (names.FirstOrDefault(name => !stack.Extras.Contains(name, StringComparer.Ordinal)) is string unknown)
            {
                throw new BadRequestException(
                    $"The resource {stack.Resource} serves no details extra named '{unknown}'.",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = unknown });
            }
        }

        /// <summary>Composes the plan on a batch.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="request">The request, its extras admitted.</param>
        /// <param name="ids">The ids in request order, distinct; null to read every row.</param>
        /// <param name="idsSource">
        ///     The table of the ids already visible, as a save's read-back passes its saved ids; null to
        ///     read the visible ids under the caller's row filter.
        /// </param>
        /// <param name="user">The connected user the batch is composed under.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>The composition, which assembles the result after the batch.</returns>
        /// <exception cref="ForbiddenException">The connected user's set grants no read of the stack.</exception>
        public DetailsComposition<TEntity, TKey> Compose<TEntity, TKey>(
            StackDescriptor stack,
            IEntityBehavior<TEntity, TKey> behavior,
            DetailsRequest request,
            IReadOnlyList<TKey>? ids,
            SqlIdentifier? idsSource,
            ConnectedUser user,
            IDataBatch batch)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(batch);

            // Every pipeline batch compiles under the pipeline's ceilings.
            batch.Limits = QueryexProfiles.PipelineLimits;
            RequestContext context = accessor.Current;
            QueryexSchemaMap map = schemas.Map(context.TenantSettings?.Shape ?? throw new InvalidOperationException(
                "The tenant's settings are not loaded in this scope yet, so the details read has no schema to compose under."));
            EntityMetadata entity = metadata.Get(stack.EntityType);
            EntityDescriptor root = map.Descriptor(entity);

            // --- The caller's read of the stack, decided once from the connected user's set ---
            DateTimeOffset now = time.GetUtcNow();
            AccessDecision read = Decide(user, stack.Resource, AccessActions.Read, now);
            if (read.Outcome == AccessOutcome.Denied)
            {
                throw new ForbiddenException(ForbiddenCode, read.Resource, read.Action, ForbiddenReason.NoGrant);
            }

            // --- Steps 1 and 2: the main ids, the rows, their collections and what they point at ---
            DetailsProjection projection = projections.For(stack, map.Schema, map.Declared(entity));
            // The producers decide what their extras show from the same set and at the same instant.
            DetailsComposition<TEntity, TKey> composition = new(
                batch,
                behavior,
                request,
                ids,
                root,
                read.Filter,
                idsSource,
                projection,
                new DetailsMaterials(map, root, materializer, metadata),
                (resource, action) => Decide(user, resource, action, now));

            // --- Step 3: the extras, each producer held to the names it declares ---
            HashSet<string> requested = new(request.Include, StringComparer.Ordinal);
            _ = requested.Remove(StackDescriptorBuilder.PermissionsExtra);
            if (requested.Count > 0)
            {
                Contribute(behavior, request, context, ids, composition, requested);
            }

            // --- Step 4: the record permissions, one keyed read per filtered grant ---
            if (request.Include.Contains(StackDescriptorBuilder.PermissionsExtra, StringComparer.Ordinal))
            {
                List<(RecordAbility, FilterTree?)> granted = [];
                foreach (RecordAbility ability in RecordAbilities.Of(stack))
                {
                    // A denied ability is the client's grant check's to show; an action without a
                    // filter is all-or-nothing, so its grant has nothing to test per row.
                    AccessDecision decision = Decide(user, stack.Resource, ability.Action, now);
                    if (decision.Outcome != AccessOutcome.Denied)
                    {
                        granted.Add((ability, ability.SupportsFilter ? decision.Filter : null));
                    }
                }

                composition.AddPermissions(granted);
            }

            return composition;
        }

        /// <summary>Asks each producer that declares a requested extra to add or omit it, then refuses a requested extra none added or omitted.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="request">The request.</param>
        /// <param name="context">The request context.</param>
        /// <param name="ids">The ids in request order; null when every row is read.</param>
        /// <param name="composition">The composition.</param>
        /// <param name="requested">The requested extras the producers serve.</param>
        /// <exception cref="InvalidOperationException">A producer declares a requested extra and neither adds nor omits it.</exception>
        private void Contribute<TEntity, TKey>(
            IEntityBehavior<TEntity, TKey> behavior,
            DetailsRequest request,
            RequestContext context,
            IReadOnlyList<TKey>? ids,
            DetailsComposition<TEntity, TKey> composition,
            HashSet<string> requested)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            // --- The service, when it declares a requested extra ---
            List<Type> producers = [];
            Type service = behavior.GetType();
            IReadOnlySet<string> serviceDeclares = DetailsExtrasNames.Of(service);
            if (requested.Overlaps(serviceDeclares))
            {
                producers.Add(service);
                behavior.ContributeDetails(Plan(composition.SinkFor(service, serviceDeclares)));
            }

            // --- Each details contributor, given the requested extras it declares ---
            foreach (IDetailsContributor<TEntity> contributor in services.GetServices<IDetailsContributor<TEntity>>())
            {
                Type type = contributor.GetType();
                IReadOnlySet<string> declares = DetailsExtrasNames.Of(type);
                HashSet<string> its = new(requested.Where(declares.Contains), StringComparer.Ordinal);
                if (its.Count > 0)
                {
                    producers.Add(type);
                    contributor.Contribute(Plan(composition.SinkFor(type, declares)), its);
                }
            }

            // --- A requested extra a producer declares but neither added nor omitted: an authoring bug ---
            if (requested.FirstOrDefault(name => !composition.Has(name)) is string missing)
            {
                string declaredBy = string.Join(", ", producers.Where(producer => DetailsExtrasNames.Of(producer).Contains(missing)).Select(static producer => producer.Name));
                throw new InvalidOperationException(
                    $"The details extra '{missing}' was requested and declared by {(declaredBy.Length == 0 ? "no producer" : declaredBy)}, but none added or omitted it. A class that declares an extra adds it, or omits it, whenever it is requested.");
            }

            // The plan each producer sees: the same request, context and ids, its own sink.
            DetailsPlan<TEntity, TKey> Plan(IDetailsPlanSink<TEntity> sink)
            {
                return new DetailsPlan<TEntity, TKey>
                {
                    Request = request,
                    Context = context,
                    IdsSource = composition.IdsSource,
                    Ids = ids ?? [],
                    Sink = sink,
                };
            }
        }

        /// <summary>The connected user's decision on one securable of the stack.</summary>
        /// <param name="user">The connected user.</param>
        /// <param name="resource">The stack's resource.</param>
        /// <param name="action">The securable action.</param>
        /// <param name="now">The moment the decision is made at.</param>
        /// <returns>The decision; a denial when the securable is not registered.</returns>
        private AccessDecision Decide(ConnectedUser user, string resource, string action, DateTimeOffset now)
        {
            return securables.Find(resource, action) is SecurableDescriptor securable
                ? evaluator.DecideFrom(user.Access, securable, now)
                : evaluator.Unknown(resource, action);
        }
    }
}
