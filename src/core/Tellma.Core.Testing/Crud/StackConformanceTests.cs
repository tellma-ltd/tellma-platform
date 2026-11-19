// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Xunit;

namespace Tellma.Core.Testing.Crud
{
    // Spec 0015 §18.3: the conformance base asserts everything it can from the stack's descriptor
    // alone; the closing class supplies valid entities, and the hooks below where a descriptor says
    // too little (a writer of the stack, a row of another entity that references it).
    /// <summary>
    ///     The executable form of a stack's contract. A distribution closes this class once per stack
    ///     by supplying a harness over its own tenant database and valid entities; the cases read
    ///     everything else from the stack's descriptor, so a stack declared differently is held to a
    ///     different contract without a line of the case changing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The cases cover what every stack shares: the declared operations reachable and the
    ///         absent ones refused; enlisted writes from a declared writer accepted and from an
    ///         undeclared one refused; the securables of the stack and its child query surfaces with
    ///         their filter roots and owners; the round-trip budget; a hidden row answering as a
    ///         missing one; stamp conflicts, overrides and missing rows; write-once and server-owned
    ///         values; null and empty collections and foreign child ids; uniqueness and rows still
    ///         referenced; the activatable, tree, cacheable and searchable recipes; and the
    ///         instruments.
    ///     </para>
    ///     <para>
    ///         A case that does not apply to the stack's declaration is skipped, naming why, and one
    ///         that needs what only the closing class can supply fails, naming the member to
    ///         override. The budget's validator-dependent rows read the members that describe the
    ///         stack's validators, since no descriptor says what a validator loads.
    ///     </para>
    ///     <para>
    ///         Every case runs against the distribution's own database (<c>Category=Integration</c>),
    ///         creates the rows it reads, and seeds the callers it connects as.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity: its leaf, or the default the leaf replaces.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    [Trait("Category", "Integration")]
    public abstract partial class StackConformanceTests<TEntity, TKey>
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The <c>activatable</c> capability.</summary>
        private const string Activatable = "activatable";

        /// <summary>The <c>tree</c> capability.</summary>
        private const string Tree = "tree";

        /// <summary>The <c>cacheable</c> capability.</summary>
        private const string Cacheable = "cacheable";

        /// <summary>The built-in action that activates rows.</summary>
        private const string ActivateAction = "activate";

        /// <summary>The built-in action that deactivates rows.</summary>
        private const string DeactivateAction = "deactivate";

        /// <summary>The activatable flag's property.</summary>
        private const string ActiveProperty = nameof(IActivatable.IsActive);

        /// <summary>The audit column a stamped root carries its stamp in.</summary>
        private const string StampProperty = "ModifiedAt";

        /// <summary>The ordinal the next entity of this stack is created with, shared by every case of the closed class.</summary>
        private static int _ordinal;

        /// <summary>The caller every write of a case's own rows runs as; seeded on first use.</summary>
        private StackConformanceCaller? _writer;

        /// <summary>The distribution's host, over its own tenant database.</summary>
        protected abstract IStackConformanceHarness Harness { get; }

        /// <summary>
        ///     The grants every seeded caller holds beside the stack's own: the securables a writer of
        ///     the stack needs on another stack, or the reads a validator's references need. Empty by
        ///     default.
        /// </summary>
        protected virtual IReadOnlyList<StackGrant> AdditionalGrants => [];

        /// <summary>
        ///     Whether the stack's validators load context on a save of the entities
        ///     <see cref="NewValidEntity" /> returns, which makes even a create with nothing of the
        ///     pipeline's own to load take a validation round trip. False by default.
        /// </summary>
        protected virtual bool ValidatorsLoad => false;

        /// <summary>
        ///     The validation rounds the stack's validators dispatch on such a save beyond the first,
        ///     each a round trip of its own. Zero by default.
        /// </summary>
        protected virtual int DependentValidationRounds => 0;

        /// <summary>
        ///     Whether a validator of the stack judges deletes, which takes a delete off its
        ///     one-round-trip path. True by default when the stack's service overrides the delete
        ///     validation hook; a stack whose component validators judge deletes says so.
        /// </summary>
        protected virtual bool ValidatesDeletes => Overrides("ValidateDeleteAsync");

        /// <summary>
        ///     Whether a validator of the stack judges actions, which takes the built-in actions off
        ///     their one-round-trip path. True by default when the stack's service overrides the action
        ///     validation hook; a stack whose component validators judge actions says so.
        /// </summary>
        protected virtual bool ValidatesActions => Overrides("ValidateActionAsync");

        /// <summary>The stack under test, as its registry describes it; read once per case.</summary>
        protected StackDescriptor Stack => field ??= FindStack();

        /// <summary>The stack's entity metadata.</summary>
        protected EntityMetadata Metadata => Harness.Services.GetRequiredService<IEntityMetadataProvider>().Get(Stack.EntityType);

        /// <summary>The running test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        ///     A new entity of the stack that a save accepts: its key default, every required value
        ///     set, and every unique and natural-key value distinct for each ordinal. Each child
        ///     collection holds at least one row, whose own unique values are distinct within the
        ///     entity.
        /// </summary>
        /// <remarks>
        ///     Two ordinals give at least one editable value that differs, other than the unique and
        ///     natural-key ones and the references, so an update can change a row.
        /// </remarks>
        /// <param name="ordinal">A positive number no other entity of this run was created with.</param>
        /// <returns>The entity.</returns>
        protected abstract TEntity NewValidEntity(int ordinal);

        /// <summary>
        ///     A new entity of a tree stack under <paramref name="parent" />. By default
        ///     <see cref="NewValidEntity" />'s entity with its parent key set to the parent's id.
        /// </summary>
        /// <param name="parent">The parent, already stored.</param>
        /// <param name="ordinal">A positive number no other entity of this run was created with.</param>
        /// <returns>The child.</returns>
        protected virtual TEntity NewChildOf(TEntity parent, int ordinal)
        {
            ArgumentNullException.ThrowIfNull(parent);
            TreeMetadata tree = Metadata.Tree ?? throw new InvalidOperationException($"The stack {Stack.Resource} is not a tree, so its entities have no parent.");
            TEntity child = NewValidEntity(ordinal);
            tree.ParentKey.Setter(child, parent.Id);
            return child;
        }

        /// <summary>
        ///     Stores an entity of the stack and answers it as stored. By default a save through the
        ///     stack's own service; a stack that serves no save stores its rows another way, and moves
        ///     its entity's version tag when it is cacheable.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The stored entity, its id filled and its values those the row holds.</returns>
        protected virtual async Task<TEntity> CreateAsync(TEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            Assert.True(
                Stack.Operations.HasFlag(StackOperations.Save),
                $"The stack {Stack.Resource} serves no save, so the suite cannot store its rows: override {nameof(CreateAsync)} to store them another way.");
            EntitiesResult<TEntity> saved = await AsWriterAsync(service => service.SaveAsync([entity], new SaveOptions(), Token));
            return Assert.Single(saved.Entities);
        }

        /// <summary>
        ///     Runs a write of another stack whose frame <paramref name="enlist" /> enlists with, in
        ///     the scope given: a save, say, whose validation hook calls <paramref name="enlist" /> with
        ///     its context's host and the service that implements the stack's
        ///     <see cref="IEnlists{TTarget}" />. The host write takes at least the round trips the
        ///     enlisted rows' own checks need, so they ride its rounds. Every stack that serves
        ///     <see cref="StackOperations.Enlist" /> overrides it; by default it fails.
        /// </summary>
        /// <param name="scope">The scope's services, connected as a caller holding every grant of the stack.</param>
        /// <param name="enlist">What to enlist, given the frame's host and a declared writer of the stack.</param>
        /// <returns>A task that completes when the host write committed.</returns>
        protected virtual Task EnlistAsync(IServiceProvider scope, Func<IWriteHost, IEnlists<TEntity>, Task> enlist)
        {
            throw new InvalidOperationException(
                $"The stack {Stack.Resource} serves enlisted writes, so the suite needs a writer of it: override {nameof(EnlistAsync)} to run a write whose frame enlists through a declared writer.");
        }

        /// <summary>
        ///     Stores a row that references <paramref name="target" /> through
        ///     <paramref name="reference" />, so a delete of the target is refused as in use. By default
        ///     only a tree's own parent key is served, by a child under the target; every other
        ///     reference needs an override.
        /// </summary>
        /// <param name="reference">The reference: an entity's foreign key whose target is the stack's entity.</param>
        /// <param name="referencing">The entity that holds the foreign key.</param>
        /// <param name="target">The stored row of the stack to reference.</param>
        /// <returns>A task that completes when the referencing row is stored.</returns>
        protected virtual async Task ReferToAsync(ReferenceMetadata reference, EntityMetadata referencing, TEntity target)
        {
            ArgumentNullException.ThrowIfNull(reference);
            ArgumentNullException.ThrowIfNull(referencing);
            ArgumentNullException.ThrowIfNull(target);
            if (referencing.ClrType == Stack.EntityType && Metadata.Tree is TreeMetadata tree && ReferenceEquals(tree.ParentKey, reference.ForeignKey))
            {
                _ = await CreateAsync(NewChildOf(target, NextOrdinal()));
                return;
            }

            Assert.Fail($"The suite cannot store a {referencing.Name} that references {Stack.Resource} through {reference.ForeignKey.Name}: override {nameof(ReferToAsync)} to store one.");
        }

        /// <summary>
        ///     The arguments a declared entity action of the stack runs with in the reachability case;
        ///     null by default, which an action whose arguments are required may refuse as invalid.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns>The arguments, as its arguments record or as JSON; null for none.</returns>
        protected virtual object? ActionArguments(EntityActionDescriptor action)
        {
            return null;
        }

        /// <summary>
        ///     Changes an entity's editable values so a save of it is a real change: by default, each
        ///     editable value that is neither the key, unique, a natural key, a reference nor the
        ///     activatable flag takes <see cref="NewValidEntity" />'s value for the ordinal.
        /// </summary>
        /// <param name="entity">The entity, as loaded.</param>
        /// <param name="ordinal">A positive number no other entity of this run was created with.</param>
        protected virtual void Change(TEntity entity, int ordinal)
        {
            ArgumentNullException.ThrowIfNull(entity);
            TEntity fresh = NewValidEntity(ordinal);
            HashSet<string> excluded = Fixed(Metadata);
            bool changed = false;
            foreach (PropertyMetadata property in Metadata.Properties)
            {
                if (property.Ownership != PropertyOwnership.Editable || excluded.Contains(property.Name))
                {
                    continue;
                }

                object? value = property.Getter(fresh);
                changed |= !Equals(property.Getter(entity), value);
                property.Setter(entity, value);
            }

            Assert.True(changed, $"{nameof(NewValidEntity)} gives the stack {Stack.Resource} the same editable values for every ordinal, so no update changes a row: vary one value that is neither unique nor a reference, or override {nameof(Change)}.");
        }

        /// <summary>The next ordinal of this stack's entities.</summary>
        /// <returns>The ordinal.</returns>
        protected int NextOrdinal()
        {
            return Interlocked.Increment(ref _ordinal);
        }

        /// <summary>The securables the stack's descriptor implies, per its operations and actions.</summary>
        /// <param name="stack">The stack.</param>
        /// <returns>Each action and its filter root, in a stable order.</returns>
        private static List<(string Action, string? FilterRoot)> ExpectedSecurables(StackDescriptor stack)
        {
            List<(string Action, string? FilterRoot)> securables = [];
            StackOperations operations = stack.Operations;

            // --- The standard operations: a cacheable stack's one shared list reads under no filter ---
            if ((operations & StackOperations.Read) != 0)
            {
                Add(AccessActions.Read, stack.Capabilities.Contains(Cacheable) ? null : stack.Resource);
            }

            if ((operations & (StackOperations.Save | StackOperations.Import)) != 0)
            {
                Add(AccessActions.Save, stack.Resource);
            }

            if (operations.HasFlag(StackOperations.Delete))
            {
                Add(AccessActions.Delete, stack.Resource);
            }

            // --- The declared actions: an entity action's grant filters its targets when it says so;
            // an API action loads no targets but is registered under the stack's filter root ---
            foreach (ActionDescriptor action in stack.Actions)
            {
                switch (action)
                {
                    case EntityActionDescriptor entity:
                        Add(entity.Action, entity.SupportsFilter ? stack.Resource : null);
                        break;
                    case ApiActionDescriptor { Securable: SecurableRef securable } when !securables.Exists(existing => existing.Action == securable.Action):
                        Add(securable.Action, stack.Resource);
                        break;
                    default:
                        break;
                }
            }

            return securables;

            void Add(string action, string? filterRoot)
            {
                if (!securables.Contains((action, filterRoot)))
                {
                    securables.Add((action, filterRoot));
                }
            }
        }

        /// <summary>The properties <see cref="Change" /> leaves alone: the key, unique values, natural keys, references and the activatable flag.</summary>
        /// <param name="entity">The entity's metadata.</param>
        /// <returns>The properties' names.</returns>
        private static HashSet<string> Fixed(EntityMetadata entity)
        {
            HashSet<string> excluded = new(StringComparer.Ordinal) { entity.Key.Name };
            excluded.UnionWith(entity.Properties.Where(static property => property.IsUnique).Select(static property => property.Name));
            excluded.UnionWith(entity.NaturalKeys.Select(static key => key.Property.Name));
            excluded.UnionWith(entity.References.Select(static reference => reference.ForeignKey.Name));
            excluded.UnionWith(entity.UniqueIndexes.SelectMany(static index => index.Properties).Select(static property => property.Name));
            if (entity.IsActivatable)
            {
                _ = excluded.Add(ActiveProperty);
            }

            return excluded;
        }

        /// <summary>An id's invariant text, as a refusal names it.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The text.</returns>
        private static string Text(TKey id)
        {
            return id.ToString(null, CultureInfo.InvariantCulture);
        }

        /// <summary>A key from a number.</summary>
        /// <param name="value">The number.</param>
        /// <returns>The key.</returns>
        private static TKey Key(long value)
        {
            return TKey.CreateChecked(value);
        }

        /// <summary>The first column of a set of rows, as keys.</summary>
        /// <param name="rows">The rows, their first column the id.</param>
        /// <returns>Each row's id.</returns>
        private static List<TKey> Keys(QueryRowSet rows)
        {
            Array buffer = rows.GetBuffer(0);
            List<TKey> keys = [];
            for (int row = 0; row < rows.RowCount; row++)
            {
                keys.Add(Key(Convert.ToInt64(buffer.GetValue(row), CultureInfo.InvariantCulture)));
            }

            return keys;
        }

        /// <summary>Each error of a validation failure as its path and code.</summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The lines, in ordinal order.</returns>
        private static List<string> Lines(ValidationException failure)
        {
            return [.. failure.Errors.Select(static error => $"{error.Path} {error.Code}").Order(StringComparer.Ordinal)];
        }

        /// <summary>The stack's service in a scope.</summary>
        /// <param name="scope">The scope's services.</param>
        /// <returns>The service.</returns>
        private static EntityService<TEntity, TKey> Service(IServiceProvider scope)
        {
            return scope.GetRequiredService<EntityService<TEntity, TKey>>();
        }

        /// <summary>Sets one of an entity's child collections.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="collection">The collection.</param>
        /// <param name="rows">The rows; null leaves the collection out of the payload.</param>
        private static void SetRows(object entity, ChildCollectionDescriptor collection, IEnumerable<object>? rows)
        {
            PropertyInfo property = entity.GetType().GetProperty(collection.Property)
                ?? throw new InvalidOperationException($"The entity {entity.GetType().Name} has no property {collection.Property}.");
            if (rows is null)
            {
                property.SetValue(entity, null);
                return;
            }

            // --- A list of the child type, as the collection's property declares it ---
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(collection.ChildType))!;
            foreach (object row in rows)
            {
                _ = list.Add(row);
            }

            property.SetValue(entity, list);
        }

        /// <summary>An id no row of the stack holds: far above any id a sequence hands out in a test run.</summary>
        /// <returns>The id.</returns>
        private TKey MissingId()
        {
            return Key(2_000_000_000L - NextOrdinal());
        }

        /// <summary>The query that reads one row's id.</summary>
        /// <param name="id">The row's id.</param>
        /// <param name="includeInactive">Whether inactive rows are read.</param>
        /// <returns>The request.</returns>
        private QueryRequest ById(TKey id, bool includeInactive = false)
        {
            return new QueryRequest { Select = Metadata.Key.Name, Filters = [$"{Metadata.Key.Name} = {Text(id)}"], IncludeInactive = includeInactive };
        }

        /// <summary>Whether the stack serves an operation, and has the capability it needs.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="capability">The capability; null when none is needed.</param>
        /// <returns>True when it serves it.</returns>
        private bool Serves(StackOperations operation, string? capability = null)
        {
            return Stack.Operations.HasFlag(operation) && (capability is null || Stack.Capabilities.Contains(capability));
        }

        /// <summary>Whether the stack serves its built-in activate and deactivate actions.</summary>
        /// <returns>True when it does.</returns>
        private bool ServesActivation()
        {
            return Stack.Actions.OfType<EntityActionDescriptor>().Any(static action => action.IsBuiltIn && action.Name == ActivateAction);
        }

        /// <summary>The stack's entry in the registry: the one whose leaf is the entity or derives from it.</summary>
        /// <returns>The stack.</returns>
        private StackDescriptor FindStack()
        {
            IStackRegistry registry = Harness.Services.GetRequiredService<IStackRegistry>();
            StackDescriptor[] stacks = [.. registry.Stacks.Where(static stack => typeof(TEntity).IsAssignableFrom(stack.EntityType))];
            return stacks.Length == 1
                ? stacks[0]
                : stacks.SingleOrDefault(static stack => stack.EntityType == typeof(TEntity))
                    ?? throw new InvalidOperationException($"The host registers {stacks.Length} stacks whose leaf is or derives from {typeof(TEntity).Name}; the suite closes over exactly one.");
        }

        /// <summary>Whether the stack's service overrides one of the base service's hooks.</summary>
        /// <param name="hook">The hook's name.</param>
        /// <returns>True when a class below the base declares it.</returns>
        private bool Overrides(string hook)
        {
            MethodInfo? method = Stack.ServiceType.GetMethod(hook, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return method is not null && method.GetBaseDefinition().DeclaringType != method.DeclaringType;
        }

        /// <summary>The caller every write of the case's own rows runs as: every securable of the stack, unfiltered.</summary>
        /// <returns>The caller.</returns>
        private async Task<StackConformanceCaller> WriterAsync()
        {
            return _writer ??= await CallerAsync();
        }

        /// <summary>
        ///     Seeds a caller holding every securable the stack's descriptor implies, the reads of the
        ///     stack's child query surfaces, and <see cref="AdditionalGrants" />: each securable of the
        ///     stack that takes a filter under <paramref name="filter" />, or none of the stack's own.
        /// </summary>
        /// <param name="filter">The filter of each grant of the stack whose securable takes one; null for none.</param>
        /// <param name="none">Whether the caller holds none of the stack's own securables, its surfaces' reads included.</param>
        /// <returns>The caller.</returns>
        private Task<StackConformanceCaller> CallerAsync(string? filter = null, bool none = false)
        {
            List<StackGrant> grants = [];
            if (!none)
            {
                grants.AddRange(ExpectedSecurables(Stack).Select(securable => new StackGrant(Stack.Resource, securable.Action, securable.FilterRoot is null ? null : filter)));
                grants.AddRange(ChildSurfaces().Select(static child => new StackGrant(child.Resource, AccessActions.Read)));
            }

            grants.AddRange(AdditionalGrants);
            return Harness.SeedCallerAsync(grants, Token);
        }

        /// <summary>The child query surfaces of the stack, at every depth.</summary>
        /// <returns>The surfaces, parents first.</returns>
        private List<ChildQueryDescriptor> ChildSurfaces()
        {
            IStackRegistry registry = Harness.Services.GetRequiredService<IStackRegistry>();
            List<ChildQueryDescriptor> surfaces = [];
            Queue<string> owners = new([Stack.Resource]);
            while (owners.TryDequeue(out string? owner))
            {
                foreach (ChildQueryDescriptor child in registry.ChildQueries.Where(child => child.OwnerResource == owner))
                {
                    surfaces.Add(child);
                    owners.Enqueue(child.Resource);
                }
            }

            return surfaces;
        }

        /// <summary>Runs a call on the stack's service in a scope of the writer.</summary>
        /// <typeparam name="T">What the call answers.</typeparam>
        /// <param name="call">The call.</param>
        /// <returns>What it answered.</returns>
        private async Task<T> AsWriterAsync<T>(Func<EntityService<TEntity, TKey>, Task<T>> call)
        {
            return (await CountedAsync(await WriterAsync(), scope => call(Service(scope)))).Result;
        }

        /// <summary>Runs a call in a scope of a caller, counting the round trips it took.</summary>
        /// <typeparam name="T">What the call answers.</typeparam>
        /// <param name="caller">The caller.</param>
        /// <param name="call">The call, given the scope's services.</param>
        /// <returns>What it answered, and its round trips.</returns>
        private async Task<Counted<T>> CountedAsync<T>(StackConformanceCaller caller, Func<IServiceProvider, Task<T>> call)
        {
            await using AsyncServiceScope scope = await Harness.ScopeAsync(caller, Token);
            IServiceProvider services = scope.ServiceProvider;
            DataAccessScope access = services.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            long? serverBefore = Harness.ServerRoundTrips(services);
            T result = await call(services);
            return new Counted<T>(result, access.RoundTrips - before, Harness.ServerRoundTrips(services) - serverBefore);
        }

        /// <summary>Runs a call in a scope of a caller, counting its round trips and recording what it threw.</summary>
        /// <param name="caller">The caller.</param>
        /// <param name="call">The call, given the stack's service.</param>
        /// <returns>What it threw, null when nothing, and its round trips.</returns>
        private Task<Counted<Exception?>> AttemptAsync(StackConformanceCaller caller, Func<EntityService<TEntity, TKey>, Task> call)
        {
            return CountedAsync(caller, async scope =>
            {
                try
                {
                    await call(Service(scope));
                    return null;
                }
#pragma warning disable CA1031 // The attempt records any refusal, which the case then judges.
                catch (Exception refusal)
#pragma warning restore CA1031
                {
                    return refusal;
                }
            });
        }

        /// <summary>Asserts a counted call's round trips, and the driver's count beside them when the harness observes it.</summary>
        /// <typeparam name="T">What the call answered.</typeparam>
        /// <param name="expected">The round trips its budget allows.</param>
        /// <param name="counted">The counted call.</param>
        /// <param name="what">The call, for the failure's message.</param>
        private void AssertTrips<T>(int expected, Counted<T> counted, string what)
        {
            Assert.True(expected == counted.RoundTrips, $"{Stack.Resource}: {what} took {counted.RoundTrips} round trips where its budget is {expected}.");
            if (counted.ServerRoundTrips is long server)
            {
                Assert.True(server == counted.RoundTrips, $"{Stack.Resource}: {what} counted {counted.RoundTrips} round trips where the driver counted {server}.");
            }
        }

        /// <summary>Loads a row of the stack by its id, with its collections, as the writer reads it.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The row.</returns>
        private async Task<TEntity> LoadAsync(TKey id)
        {
            EntitiesResult<TEntity> loaded = await AsWriterAsync(service => service.GetByIdAsync(id, DetailsRequest.None, Token));
            return Assert.Single(loaded.Entities);
        }

        /// <summary>Whether a row of the stack exists, as the writer, who sees every row, reads it.</summary>
        /// <param name="id">The id.</param>
        /// <param name="includeInactive">Whether an inactive row counts.</param>
        /// <returns>True when it exists.</returns>
        private async Task<bool> ExistsAsync(TKey id, bool includeInactive = true)
        {
            RowPage page = await AsWriterAsync(service => service.QueryAsync(ById(id, includeInactive), Token));
            return page.Rows.RowCount == 1;
        }

        /// <summary>Stores a new valid entity.</summary>
        /// <returns>The stored entity.</returns>
        private Task<TEntity> CreateNewAsync()
        {
            return CreateAsync(NewValidEntity(NextOrdinal()));
        }

        /// <summary>Saves entities as the writer.</summary>
        /// <param name="entities">The entities.</param>
        /// <param name="mode">How stamps are compared.</param>
        /// <returns>The rows read back.</returns>
        private Task<EntitiesResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, ConcurrencyMode mode = ConcurrencyMode.Check)
        {
            return AsWriterAsync(service => service.SaveAsync(entities, new SaveOptions { Concurrency = mode }, Token));
        }

        /// <summary>A mapped property of the stack's entity.</summary>
        /// <param name="name">The property's name.</param>
        /// <returns>The property.</returns>
        private PropertyMetadata Property(string name)
        {
            return Metadata.Properties.Single(property => property.Name == name);
        }

        /// <summary>One of the entity's child collections, as its metadata describes it.</summary>
        /// <param name="collection">The collection's descriptor.</param>
        /// <returns>The collection's metadata.</returns>
        private ChildCollectionMetadata Collection(ChildCollectionDescriptor collection)
        {
            return Metadata.Children.Single(child => child.Property == collection.Property);
        }

        /// <summary>What a counted call answered, and the round trips it took.</summary>
        /// <typeparam name="T">What it answered.</typeparam>
        /// <param name="Result">What it answered.</param>
        /// <param name="RoundTrips">The round trips the platform counted.</param>
        /// <param name="ServerRoundTrips">The round trips the driver counted; null when the harness does not observe it.</param>
        private readonly record struct Counted<T>(T Result, int RoundTrips, long? ServerRoundTrips);
    }
}
