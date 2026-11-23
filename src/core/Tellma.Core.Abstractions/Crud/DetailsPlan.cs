// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     A details read's plan as an extras producer sees it, without the key type: the request, the
    ///     visible ids as a table a statement joins, and the means to add extras to the details batch.
    ///     A component generic over the entity alone sees this view; the service sees
    ///     <see cref="DetailsPlan{TEntity, TKey}" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An extra takes one of two forms. A statement (<see cref="AddExtra(string, FormattableString, SqlOptions)" />
    ///         or <see cref="AddExtra(string, QuerySpec, QueryArguments)" />) whose result set becomes the
    ///         extra; or a computed extra (<see cref="AddComputedExtra{TValue}" />), C# that runs once
    ///         after the batch over the visible entities and the results of the producer's
    ///         <see cref="AddRead(QuerySpec, QueryArguments)" /> statements. Neither adds a round trip.
    ///     </para>
    ///     <para>
    ///         An extra's name must be one the read includes and the producer's class declares, and is
    ///         added once; a raw statement declares no writes. A breach of either rule is an
    ///         <see cref="InvalidOperationException" />: an authoring bug.
    ///     </para>
    ///     <para>
    ///         A producer adds every requested extra it declares, or leaves it out on purpose with
    ///         <see cref="OmitExtra" />, as when <see cref="Decide" /> denies the caller what the extra
    ///         would show; an omitted extra is absent from the result.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public abstract class DetailsPlan<TEntity>
    {
        /// <summary>Initialises the view; private protected so the keyed plan is its one kind.</summary>
        private protected DetailsPlan()
        {
        }

        /// <summary>The read's request: the extras it includes.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required DetailsRequest Request
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Request));
        }

        /// <summary>Who is reading, in which tenant and culture.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required RequestContext Context
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Context));
        }

        /// <summary>
        ///     The visible ids, as the only table an extra's statement may join: the ids the read's
        ///     access filter admitted, or the saved ids on a save's read-back. Opaque: interpolate it,
        ///     never spell its name. Empty for a read whose id is invisible, so an extra over it yields
        ///     no rows.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required SqlIdentifier IdsSource
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(IdsSource));
        }

        /// <summary>The planner the producer's calls go to, which enforces the name rules; set by the pipeline.</summary>
        internal IDetailsPlanSink<TEntity>? Sink { get; init; }

        /// <summary>Adds an extra as a raw statement, its first result set becoming the extra.</summary>
        /// <param name="name">The extra's name.</param>
        /// <param name="sql">The statement, its values as interpolated parameters; it may join <see cref="IdsSource" />.</param>
        /// <param name="options">The statement's options, declaring no writes; null for none.</param>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or white space.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="sql" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The name is not requested or not declared, was added already, or the options declare writes.</exception>
        public void AddExtra(string name, FormattableString sql, SqlOptions? options)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(sql);
            Planner().AddExtra(name, sql, options);
        }

        /// <summary>Adds an extra as a query, its row set becoming the extra.</summary>
        /// <param name="name">The extra's name.</param>
        /// <param name="spec">The query; it may restrict by <see cref="IdsSource" />.</param>
        /// <param name="arguments">The values of the parameters the query mentions; null for none.</param>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or white space.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="spec" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The name is not requested or not declared, or was added already.</exception>
        public void AddExtra(string name, QuerySpec spec, QueryArguments? arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(spec);
            Planner().AddExtra(name, spec, arguments);
        }

        /// <summary>Adds a query whose result only a computed extra reads; it is never sent to the caller.</summary>
        /// <param name="spec">The query; it may restrict by <see cref="IdsSource" />.</param>
        /// <param name="arguments">The values of the parameters the query mentions; null for none.</param>
        /// <returns>The query's result, available once the details batch ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="spec" /> is null.</exception>
        public BatchResult<RowPage> AddRead(QuerySpec spec, QueryArguments? arguments)
        {
            ArgumentNullException.ThrowIfNull(spec);
            return Planner().AddRead(spec, arguments);
        }

        /// <summary>Adds a raw statement whose result only a computed extra reads; it is never sent to the caller.</summary>
        /// <param name="sql">The statement, its values as interpolated parameters; it may join <see cref="IdsSource" />.</param>
        /// <param name="options">The statement's options, declaring no writes; null for none.</param>
        /// <returns>The statement's result sets, available once the details batch ran.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="sql" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The options declare writes.</exception>
        public BatchResult<RawResult> AddRead(FormattableString sql, SqlOptions? options)
        {
            ArgumentNullException.ThrowIfNull(sql);
            return Planner().AddRead(sql, options);
        }

        /// <summary>
        ///     Adds an extra computed in C# once after the details batch, over the visible entities in
        ///     request order; its value is serialised under the platform's JSON options.
        /// </summary>
        /// <typeparam name="TValue">The extra's value type.</typeparam>
        /// <param name="name">The extra's name.</param>
        /// <param name="compute">Computes the value from the visible entities and the producer's reads.</param>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or white space.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="compute" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The name is not requested or not declared, or was added already.</exception>
        public void AddComputedExtra<TValue>(string name, Func<IReadOnlyList<TEntity>, TValue> compute)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(compute);
            Planner().AddComputedExtra(name, compute);
        }

        /// <summary>
        ///     Adds an extra computed in C# once after the details batch, over the visible entities in
        ///     request order and the rows of a query of another entity: the query's own columns fill
        ///     instances of <typeparamref name="TRow" />, in row order, and the columns it selects
        ///     through navigations join the read's related entities, each entity once. Its value is
        ///     serialised under the platform's JSON options.
        /// </summary>
        /// <remarks>
        ///     The query is one statement of the details batch, rooted at <typeparamref name="TRow" />'s
        ///     entity; a related column is selected with its navigation's key, so the related entity can
        ///     be found by id.
        /// </remarks>
        /// <typeparam name="TRow">The query's entity type.</typeparam>
        /// <typeparam name="TValue">The extra's value type.</typeparam>
        /// <param name="name">The extra's name.</param>
        /// <param name="spec">The query; it may restrict by <see cref="IdsSource" />.</param>
        /// <param name="arguments">The values of the parameters the query mentions; null for none.</param>
        /// <param name="compute">Computes the value from the visible entities and the query's rows.</param>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or white space.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="spec" /> or <paramref name="compute" /> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The name is not requested or not declared, or was added already; or the query is not
        ///     rooted at <typeparamref name="TRow" />'s entity.
        /// </exception>
        public void AddComputedExtra<TRow, TValue>(
            string name, QuerySpec spec, QueryArguments? arguments, Func<IReadOnlyList<TEntity>, IReadOnlyList<TRow>, TValue> compute)
            where TRow : class
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(compute);
            Planner().AddComputedExtra(name, spec, arguments, compute);
        }

        /// <summary>
        ///     Leaves a requested extra out of the result on purpose, as when the caller may not read
        ///     what it would show: no statement is added and the extra is absent. By the name rules of
        ///     <see cref="AddExtra(string, QuerySpec, QueryArguments)" />, an extra is added or omitted
        ///     once.
        /// </summary>
        /// <param name="name">The extra's name.</param>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or white space.</exception>
        /// <exception cref="InvalidOperationException">The name is not requested or not declared, or was added or omitted already.</exception>
        public void OmitExtra(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Planner().OmitExtra(name);
        }

        /// <summary>
        ///     The caller's decision on a securable, from the permission set the details read is
        ///     composed under, as the read's own check was decided: an extra showing rows of another
        ///     resource reads them under this decision's filter, and is omitted when it denies.
        ///     Nothing is recorded as evaluated and nothing is counted.
        /// </summary>
        /// <param name="resource">The securable's resource, or a former name of it.</param>
        /// <param name="action">The securable's action.</param>
        /// <returns>The decision; a denial with its problem when the registry does not hold the pair.</returns>
        /// <exception cref="ArgumentException"><paramref name="resource" /> or <paramref name="action" /> is null, empty or white space.</exception>
        public AccessDecision Decide(string resource, string action)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            return Planner().Decide(resource, action);
        }

        /// <summary>The planner behind the plan.</summary>
        /// <returns>The planner.</returns>
        /// <exception cref="InvalidOperationException">The plan was not prepared by the details planner.</exception>
        private IDetailsPlanSink<TEntity> Planner()
        {
            return Sink ?? throw new InvalidOperationException("The details plan was not prepared by the details planner, so it cannot take extras.");
        }
    }

    /// <summary>A details read's plan as the service sees it: the key-erased view and the requested ids.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    public sealed class DetailsPlan<TEntity, TKey> : DetailsPlan<TEntity>
    {
        /// <summary>The requested ids, in request order; some may be invisible.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<TKey> Ids
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Ids));
        }
    }

    /// <summary>
    ///     The details planner's side of a plan: it records each extra and read on the details batch
    ///     under the name rules, and decides securables from the set the batch is composed under.
    /// </summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    internal interface IDetailsPlanSink<TEntity>
    {
        /// <summary>Records an extra as a raw statement.</summary>
        /// <param name="name">The extra's name.</param>
        /// <param name="sql">The statement.</param>
        /// <param name="options">The statement's options.</param>
        void AddExtra(string name, FormattableString sql, SqlOptions? options);

        /// <summary>Records an extra as a query.</summary>
        /// <param name="name">The extra's name.</param>
        /// <param name="spec">The query.</param>
        /// <param name="arguments">The query's arguments.</param>
        void AddExtra(string name, QuerySpec spec, QueryArguments? arguments);

        /// <summary>Records a query only a computed extra reads.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="arguments">The query's arguments.</param>
        /// <returns>The query's result.</returns>
        BatchResult<RowPage> AddRead(QuerySpec spec, QueryArguments? arguments);

        /// <summary>Records a raw statement only a computed extra reads.</summary>
        /// <param name="sql">The statement.</param>
        /// <param name="options">The statement's options.</param>
        /// <returns>The statement's result sets.</returns>
        BatchResult<RawResult> AddRead(FormattableString sql, SqlOptions? options);

        /// <summary>Records a computed extra.</summary>
        /// <typeparam name="TValue">The extra's value type.</typeparam>
        /// <param name="name">The extra's name.</param>
        /// <param name="compute">Computes the value.</param>
        void AddComputedExtra<TValue>(string name, Func<IReadOnlyList<TEntity>, TValue> compute);

        /// <summary>Records a computed extra over the rows of a query of another entity, whose related columns join the read's related entities.</summary>
        /// <typeparam name="TRow">The query's entity type.</typeparam>
        /// <typeparam name="TValue">The extra's value type.</typeparam>
        /// <param name="name">The extra's name.</param>
        /// <param name="spec">The query.</param>
        /// <param name="arguments">The query's arguments.</param>
        /// <param name="compute">Computes the value.</param>
        void AddComputedExtra<TRow, TValue>(string name, QuerySpec spec, QueryArguments? arguments, Func<IReadOnlyList<TEntity>, IReadOnlyList<TRow>, TValue> compute)
            where TRow : class;

        /// <summary>Records a requested extra left out on purpose.</summary>
        /// <param name="name">The extra's name.</param>
        void OmitExtra(string name);

        /// <summary>Decides a securable from the permission set the batch is composed under.</summary>
        /// <param name="resource">The securable's resource.</param>
        /// <param name="action">The securable's action.</param>
        /// <returns>The decision.</returns>
        AccessDecision Decide(string resource, string action);
    }
}
