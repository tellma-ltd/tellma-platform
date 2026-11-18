// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Numerics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Crud.Query;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Delete
{
    // A delete is spec 0015 §10: the ceilings and the type-level check before any round trip (§9.1),
    // then one Persist batch composing spec 0012's DeleteSpec, which checks the ids under the caller's
    // Read filter and the Delete grant inside the transaction (§9.2), the access guards (§6.7 step 4),
    // the participants over a DeletePersistContext (§13.1), and the post-commit phase (§13.2). A delete
    // by ids or with descendants whose service or a component validates deletes first runs round trip
    // 1, the same two stages in C#, and the validators' rounds (§10.1). A delete by query counts the
    // rows it matches beside its delete, so its cap and its count report the rows matched (§10.2).
    // The same run serves a delete by ids enlisted with another operation's frame (§13.3): that mode
    // is the enlisted part.
    /// <summary>One delete of one stack: by ids, by query or with descendants.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    /// <param name="operation">The scope's write services.</param>
    /// <param name="stack">The stack.</param>
    /// <param name="behavior">The service's hooks.</param>
    /// <param name="operationName">The <c>crud.operation</c> the delete runs as.</param>
    /// <param name="cancellationToken">Cancels the delete's validation rounds; a persist already sent completes regardless.</param>
    /// <param name="site">The frame the delete is enlisted with and how; null for a delete of its own.</param>
    internal sealed partial class DeleteRun<TEntity, TKey>(
        SaveOperation operation,
        StackDescriptor stack,
        IEntityBehavior<TEntity, TKey> behavior,
        string operationName,
        CancellationToken cancellationToken,
        EnlistmentSite? site = null)
        : WriteRun(operation, stack.Resource, operationName), IEnlistedDelete
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The request property the ids are reported at.</summary>
        private const string IdsProperty = "Ids";

        /// <summary>The location a diagnostic in a delete by query's filter is reported at.</summary>
        private const string FilterLocation = "filter";

        /// <summary>The location the engine reports a diagnostic in the composed filter at, the spec's own property.</summary>
        private const string ComposedFilter = "Filter";

        /// <summary>The stack.</summary>
        private readonly StackDescriptor _stack = stack;

        /// <summary>The service's hooks.</summary>
        private readonly IEntityBehavior<TEntity, TKey> _behavior = behavior;

        /// <summary>Cancels the delete's validation rounds.</summary>
        private readonly CancellationToken _cancellationToken = cancellationToken;

        /// <summary>The stack's root entity.</summary>
        private readonly EntityMetadata _root = operation.Metadata.Get(typeof(TEntity));

        /// <summary>The frame the delete is enlisted with and how; null for a delete of its own.</summary>
        private readonly EnlistmentSite? _site = site;

        /// <summary>The delete's errors, the validators': its own list, or the view that re-homes them into its host's.</summary>
        private readonly ValidationErrors<TEntity> _errors = site is null
            ? new()
            : new ValidationErrors<TEntity>(site.Host.Errors, site.MapPath, stack.Resource);

        /// <summary>The frame a hooked delete of its own runs its validators in; null until they run, and for an enlisted delete.</summary>
        private PipelineFrame? _frame;

        /// <summary>The delete's shape.</summary>
        private DeleteShape _shape;

        /// <summary>The target ids, each once, in request order; empty for a delete by query.</summary>
        private TKey[] _ids = [];

        /// <summary>The stamp each target was read with, parallel to <see cref="_ids" />; null to skip the comparison.</summary>
        private DateTimeOffset?[]? _stamps;

        /// <summary>A delete by query's filter text, as the caller sent it.</summary>
        private string? _filter;

        /// <summary>A delete by query's bound arguments.</summary>
        private QueryArguments? _arguments;

        /// <summary>The count a delete by query's caller confirmed.</summary>
        private int _expectedCount;

        /// <summary>The counts the delete by query composed last, for the translation of its cap and its count.</summary>
        private QueryDeleteCounts? _counts;

        /// <summary>The tenant's schema map.</summary>
        private QueryexSchemaMap _map = null!;

        /// <summary>Whether the service or a component validates deletes, so round trip 1 runs.</summary>
        private bool _hooked;

        /// <summary>The context the validators see; null on the one-round-trip path.</summary>
        private DeleteContext<TEntity, TKey>? _context;

        /// <summary>The operation's loader; null on the one-round-trip path.</summary>
        private ContextLoader? _loader;

        /// <summary>The visible targets round trip 1 loaded, and the Delete grant's count over them.</summary>
        private (IReadOnlyList<TEntity> Rows, int? Granted) _loaded = ([], null);

        /// <summary>The targets, in request order, once round trip 1 settled them.</summary>
        private IReadOnlyList<TEntity> _targets = [];

        /// <summary>The times the runner entered the persist's compose.</summary>
        private int _entries;

        /// <summary>The delete's handle of the first entry, whose names the participants hold.</summary>
        private DeleteHandle? _firstHandle;

        /// <summary>The delete's handle of the batch composed last.</summary>
        private DeleteHandle? _handle;

        /// <summary>The recording of the service hook's and the effects' appends.</summary>
        private RecordingBatch? _participants;

        /// <summary>The tables of the stack: the root's and every child collection's, at every depth.</summary>
        private IReadOnlySet<TableName> StackTables => field ??= StackTableSet.Of(_root);

        /// <summary>The rounds the groups enlisted with a delete of its own run in; null while none can have enlisted.</summary>
        private EnlistmentRounds? Enlistments => _frame is { HasRounds: true } frame ? frame.Rounds : null;

        /// <summary>Deletes rows by their ids, all or nothing.</summary>
        /// <param name="ids">The ids.</param>
        /// <param name="expectedStamps">The stamp each row was read with, parallel to the ids; null to skip the comparison.</param>
        /// <returns>The rows deleted.</returns>
        public async Task<AffectedResult> ByIdsAsync(IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps)
        {
            // --- The ceilings, then the type-level grant, then the stamps, before any round trip ---
            _shape = DeleteShape.ByIds;
            CheckIds(ids);
            if (expectedStamps is not null && expectedStamps.Count != ids.Count)
            {
                throw new BadRequestException(
                    string.Create(CultureInfo.InvariantCulture, $"The expected stamps are parallel to the ids: {expectedStamps.Count} stamps for {ids.Count} ids."),
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["ids"] = ids.Count, ["stamps"] = expectedStamps.Count });
            }

            // The data layer compares stamps of int keys alone, so a long-keyed stack takes its
            // deletes without them; refused here rather than by the data layer once composing.
            if (expectedStamps is not null && typeof(TKey) == typeof(long))
            {
                throw new BadRequestException(
                    $"The deletes of {Resource} compare no stamps: its keys are long. Delete its rows without expected stamps.",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["resource"] = Resource });
            }

            _ = await Services.Evaluator.RequireAsync(Resource, AccessActions.Delete, _cancellationToken).ConfigureAwait(false);
            if (expectedStamps is not null)
            {
                RequireStamps(expectedStamps);
            }

            // --- Each id once, with the stamp it was first named with ---
            _ids = DistinctIds(ids);
            if (expectedStamps is not null)
            {
                Dictionary<TKey, DateTimeOffset?> stamps = [];
                for (int index = 0; index < ids.Count; index++)
                {
                    _ = stamps.TryAdd(ids[index], expectedStamps[index]);
                }

                _stamps = [.. _ids.Select(id => stamps[id])];
            }

            if (_ids.Length == 0)
            {
                return new AffectedResult(0);
            }

            _map = Map();
            return await RunAsync().ConfigureAwait(false);
        }

        /// <summary>Deletes tree roots with every descendant, all or nothing.</summary>
        /// <param name="ids">The roots' ids.</param>
        /// <returns>The rows deleted, descendants included.</returns>
        public async Task<AffectedResult> WithDescendantsAsync(IReadOnlyList<TKey> ids)
        {
            _shape = DeleteShape.WithDescendants;
            CheckIds(ids);
            _ = await Services.Evaluator.RequireAsync(Resource, AccessActions.Delete, _cancellationToken).ConfigureAwait(false);
            _ids = DistinctIds(ids);
            if (_ids.Length == 0)
            {
                return new AffectedResult(0);
            }

            _map = Map();
            return await RunAsync().ConfigureAwait(false);
        }

        /// <summary>Deletes the rows a filter selects, capped, and only when they number what the caller confirmed.</summary>
        /// <param name="request">The filter, its arguments and the confirmed count.</param>
        /// <param name="engine">The Queryex engine, which discovers the filter's parameters and compiles it before any round trip.</param>
        /// <returns>The rows deleted.</returns>
        public async Task<AffectedResult> ByQueryAsync(DeleteByQueryRequest request, QueryexEngine engine)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(engine);

            // --- A blank filter is "delete all", never exposed; then the type-level grant ---
            _shape = DeleteShape.ByQuery;
            if (string.IsNullOrWhiteSpace(request.Filter))
            {
                throw new BadRequestException("A delete by query names the rows it deletes: its filter is blank.");
            }

            if (request.ExpectedCount < 0)
            {
                throw new BadRequestException(
                    "The expected count is the number of rows the caller confirmed, zero or more.",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["expectedCount"] = request.ExpectedCount });
            }

            AccessDecision decision = await Services.Evaluator.RequireAsync(Resource, AccessActions.Delete, _cancellationToken).ConfigureAwait(false);

            // --- The filter's parameters and the compilation, so a caller's mistake is refused before any round trip ---
            _filter = request.Filter;
            _expectedCount = request.ExpectedCount;
            _map = Map();
            _arguments = Compile(engine, request, decision);
            return await RunAsync().ConfigureAwait(false);
        }

        /// <summary>Validates when the stack validates deletes, then persists.</summary>
        /// <returns>The rows deleted.</returns>
        private async Task<AffectedResult> RunAsync()
        {
            // A delete by query names no ids the validators could see, so it runs no validation round.
            _hooked = _shape != DeleteShape.ByQuery
                && HookOverrides.ValidatesDeletes(_behavior, Services.Services.GetServices<IEntityValidator<TEntity>>());
            if (!_hooked)
            {
                return await PersistAsync().ConfigureAwait(false);
            }

            // --- Round trip 1 and the validators' rounds, the groups enlisted from them closed with
            // them, then the outcome in the order a caller can act on ---
            using ConfirmationFrame frame = Services.Confirmations.Frame(Resource, OperationName);
            try
            {
                await ValidateAsync(frame).ConfigureAwait(false);
                _frame!.Close(complete: true);
                ValidationOutcome.Raise(staleStamp: null, _errors, frame);
                AffectedResult result = await PersistAsync().ConfigureAwait(false);
                Enlistments?.Count(failure: null);
                return result;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Enlistments?.Count(failure);
                throw;
            }
            finally
            {
                // The groups enlisted with the delete return the ids they reserved and never sent.
                _frame?.Close();
                Enlistments?.Release();
            }
        }

        /// <summary>The tenant's schema map, for the shape the tenant's settings configure.</summary>
        /// <returns>The map.</returns>
        /// <exception cref="InvalidOperationException">The tenant's settings are not loaded in this scope.</exception>
        private QueryexSchemaMap Map()
        {
            MultilingualShape shape = Services.Accessor.Current.TenantSettings?.Shape
                ?? throw new InvalidOperationException("The tenant's settings are not loaded in this scope, so the delete has no schema to compose under.");
            return Services.Schemas.Map(shape);
        }

        /// <summary>Refuses more ids than the stack's ceiling.</summary>
        /// <param name="ids">The ids as sent.</param>
        /// <exception cref="LimitExceededException">More ids than the ceiling.</exception>
        private void CheckIds(IReadOnlyList<TKey> ids)
        {
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count > _stack.Limits.MaxIds)
            {
                throw new LimitExceededException(LimitNames.MaxIds, ids.Count, _stack.Limits.MaxIds);
            }
        }

        /// <summary>Refuses a null stamp beside an id, at the id's position.</summary>
        /// <param name="stamps">The stamps, parallel to the ids as sent.</param>
        /// <exception cref="ValidationException">A stamp is null.</exception>
        private static void RequireStamps(IReadOnlyList<DateTimeOffset?> stamps)
        {
            ValidationError[] missing = [.. stamps
                .Select(static (stamp, index) => (stamp, index))
                .Where(static entry => entry.stamp is null)
                .Select(static entry => new ValidationError(
                    ValidationPath.Root.Property(IdsProperty).Index(entry.index),
                    ValidationCodes.ConcurrencyStampRequired,
                    new Dictionary<string, object?>(StringComparer.Ordinal)))];
            if (missing.Length > 0)
            {
                throw new ValidationException(missing);
            }
        }

        // Spec 0015 §10.2: the filter compiles as And(Leaf(filter), decision.Filter) with the request's
        // arguments, and no activatable conjunct. Its parameters are discovered from the caller's text
        // alone, as a query's are (§4.3), and the compilation runs here, under the pipeline's ceilings,
        // so a filter that does not compile is the caller's 400 rather than a failure at compose time.
        /// <summary>Binds the filter's arguments and compiles the delete's filter under the decision taken now.</summary>
        /// <param name="engine">The Queryex engine.</param>
        /// <param name="request">The request.</param>
        /// <param name="decision">The Delete decision.</param>
        /// <returns>The bound arguments.</returns>
        /// <exception cref="BadRequestException">An argument does not convert, or two names differ only in case.</exception>
        /// <exception cref="InvalidQueryException">The filter does not compile.</exception>
        private QueryArguments Compile(QueryexEngine engine, DeleteByQueryRequest request, AccessDecision decision)
        {
            // --- The parameters the caller's filter mentions, bound from the JSON ---
            EntityDescriptor root = _map.Descriptor(_root);
            DiscoveryResult discovery = engine.DiscoverQuery(
                new QueryDiscoverySpec { Filter = FilterTree.Leaf(request.Filter) },
                new DiscoveryOptions { Schema = _map.Schema, Root = root, QueryexVersion = QueryexLanguage.Version, Limits = QueryexProfiles.PipelineLimits });
            QueryArguments arguments = QueryArgumentValues.Bind(QueryDiscoveries.Declarations(discovery), ByName(request.Arguments));

            // --- The composed filter, compiled as the batch compiles it ---
            QueryexResult<CompiledQuery> compiled = engine.CompileQuery(
                new QuerySpec { Root = root, Select = root.Key.Name, Filter = Filter(decision) },
                new QueryCompilationOptions
                {
                    Schema = _map.Schema,
                    QueryexVersion = QueryexLanguage.Version,
                    Parameters = arguments.Declarations,
                    OmittedParameters = arguments.Omitted,
                    HasUser = Services.Accessor.Current.UserId is not null,
                    Limits = QueryexProfiles.PipelineLimits,
                });
            if (compiled.Value is null)
            {
                Conjunct[] conjuncts = decision.Filter is null
                    ? [new Conjunct(ConjunctKind.UserFilter, 0)]
                    : [new Conjunct(ConjunctKind.UserFilter, 0), new Conjunct(ConjunctKind.Platform, -1)];
                IReadOnlyList<QueryexDiagnostic> located = QueryDiagnostics.Locate(compiled.Diagnostics, new CompositionMap(conjuncts, SelectSent: false, OrderingSource.Default), Resource);

                // The request carries one filter, so a location in it is the filter's own: the caller's
                // conjunct when the grant's filter joins it, else the composed filter itself.
                string sent = QueryDiagnostics.UserFilter(0);
                throw new InvalidQueryException([.. located.Select(diagnostic => diagnostic.Location switch
                {
                    string location when location.StartsWith(sent, StringComparison.Ordinal) => diagnostic with { Location = FilterLocation + location[sent.Length..] },
                    string location when decision.Filter is null && location.StartsWith(ComposedFilter, StringComparison.Ordinal)
                        => diagnostic with { Location = FilterLocation + location[ComposedFilter.Length..] },
                    _ => diagnostic,
                })]);
            }

            return arguments;
        }

        /// <summary>The delete by query's filter: the caller's leaf, conjoined with the Delete grant's filter.</summary>
        /// <param name="decision">The Delete decision.</param>
        /// <returns>The filter.</returns>
        private FilterTree Filter(AccessDecision decision)
        {
            var sent = FilterTree.Leaf(_filter!);
            return decision.Filter is FilterTree granted ? FilterTree.And([sent, granted]) : sent;
        }

        /// <summary>The caller's arguments by parameter name ignoring case.</summary>
        /// <param name="arguments">The arguments as sent; null for none.</param>
        /// <returns>The arguments.</returns>
        /// <exception cref="BadRequestException">Two names differ only in case.</exception>
        private static Dictionary<string, System.Text.Json.JsonElement> ByName(IReadOnlyDictionary<string, System.Text.Json.JsonElement>? arguments)
        {
            Dictionary<string, System.Text.Json.JsonElement> byName = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, System.Text.Json.JsonElement value) in arguments ?? new Dictionary<string, System.Text.Json.JsonElement>())
            {
                if (!byName.TryAdd(name, value))
                {
                    throw new BadRequestException(
                        string.Create(CultureInfo.InvariantCulture, $"The arguments name the parameter '{name}' twice, in two cases."),
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["parameter"] = name });
                }
            }

            return byName;
        }
    }

    /// <summary>The shape of a delete.</summary>
    internal enum DeleteShape
    {
        /// <summary>Rows by their ids.</summary>
        ByIds,

        /// <summary>The rows a filter selects.</summary>
        ByQuery,

        /// <summary>Tree roots with every descendant.</summary>
        WithDescendants,
    }
}
