// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Details
{
    /// <summary>
    ///     The details reads over the scripted batch: the plan's statements in one round trip, the
    ///     visible rows in request order, what their lines point at, the extras and their name rules,
    ///     the record permissions, the ceilings refused before any round trip, the rows of a tree by
    ///     parent, and every row of a cacheable stack from the cache.
    /// </summary>
    public sealed class DetailsOperationTests : IAsyncDisposable
    {
        /// <summary>The grant filter of the save, which reaches order 1 only.</summary>
        private const string SaveFilter = "Code != 'save'";

        /// <summary>The grant filter of the post action, which reaches every order.</summary>
        private const string PostFilter = "Name != 'post'";

        /// <summary>The cache the rig's cacheable list is read from.</summary>
        private readonly FakeCache _cache = new();

        /// <summary>The details model.</summary>
        private readonly QueryRig _rig;

        /// <summary>Composes the details model with the fake cache.</summary>
        public DetailsOperationTests()
        {
            _rig = new QueryRig(DetailsModel.Compose, services => services.Replace(ServiceDescriptor.Singleton<ICacheableEntities>(_cache)));
        }

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- The plan's statements and the rows in request order ---

        [Fact]
        public async Task A_read_by_ids_reads_the_visible_ids_the_rows_and_what_their_lines_point_at_in_one_round_trip_in_request_order_hidden_ids_left_out()
        {
            _rig.Grant((DetailsModel.Orders, AccessActions.Read, "Code != 'hidden'"));
            Script(main: [1, 3], orders: [Order(1, "A"), Order(3, "B")]);

            await using AsyncServiceScope scope = _rig.Scope();
            EntitiesResult<DmOrder> result = await Orders(scope).GetByIdsAsync([2, 3, 1], new DetailsRequest(), Token);

            // --- One round trip: the main ids under the read filter, the rows, the lines' targets ---
            ScriptedBatch batch = Assert.Single(_rig.Script.Batches);
            Assert.Equal((BatchPurpose.Read, 1), (batch.Purpose, _rig.Script.RoundTrips));
            ScriptedStatement main = Assert.Single(batch.Of(ScriptedKind.Rows), static statement => statement.Options is RowQueryOptions { CaptureKeys: true });
            Assert.Equal("Id", main.Spec.Select);
            Assert.Contains("Code != 'hidden'", QueryRig.Render(main.Spec.Filter), StringComparison.Ordinal);
            Assert.Equal([2, 3, 1], main.KeySet<IdList>().Select(static row => row.Id));
            Assert.Same(QueryexProfiles.PipelineLimits, batch.Limits);

            string captured = $"@tb{main.Ordinal}_keys";
            EntityQuery<DmOrder> query = Assert.Single(batch.Of(ScriptedKind.Query)).Query<DmOrder>();
            Assert.True(query.AllProperties);
            Assert.Equal(["Lines"], query.Children);
            Assert.Equal(("Id", captured), (Assert.Single(query.Restrictions).Path, query.Restrictions[0].TableSource));
            Assert.Contains("Customer.Id", query.Select, StringComparison.Ordinal);

            QuerySpec targets = Assert.Single(batch.Of(ScriptedKind.Rows), static statement => statement.Spec.Aggregate).Spec;
            Assert.Equal(DetailsModel.Orders + "Line", targets.Root.Name);
            Assert.StartsWith("ShipTo.Id", targets.Select, StringComparison.Ordinal);
            Assert.Equal(("OrderId", captured), (Assert.Single(targets.Restrictions).Path, targets.Restrictions[0].TableSource));

            // --- The visible rows in request order, and the lines' customers each once ---
            Assert.Equal([3L, 1L], result.Ids);
            Assert.Equal([3, 1], result.Entities.Select(static order => order.Id));
            Assert.Null(result.Extras);
            RelatedEntitySet customers = Assert.Single(result.Related!.Sets.Values, static set => set.EntityType == typeof(DmCustomer));
            Assert.Equal([7, 8], customers.Entities.Cast<DmCustomer>().Select(static customer => customer.Id).Order());
        }

        [Fact]
        public async Task A_read_of_one_id_that_is_missing_or_invisible_is_not_found()
        {
            _rig.Grant((DetailsModel.Orders, AccessActions.Read, null));
            Script(main: [], orders: []);

            await using AsyncServiceScope scope = _rig.Scope();
            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => Orders(scope).GetByIdAsync(9, new DetailsRequest(), Token));

            Assert.Equal((DetailsModel.Orders, "9"), (missing.Resource, Assert.Single(missing.Ids)));
            _ = Assert.Single(_rig.Script.Batches);
        }

        [Fact]
        public async Task A_read_without_the_grant_or_beyond_the_ceilings_is_refused_before_any_round_trip()
        {
            _rig.Grant((DetailsModel.Customers, AccessActions.Read, null));
            await using (AsyncServiceScope denied = _rig.Scope())
            {
                _ = await Assert.ThrowsAsync<ForbiddenException>(() => Orders(denied).GetByIdAsync(1, new DetailsRequest(), Token));
            }

            _rig.Grant((DetailsModel.Orders, AccessActions.Read, null));
            await using AsyncServiceScope scope = _rig.Scope();
            DmOrderService orders = Orders(scope);
            LimitExceededException ids = await Assert.ThrowsAsync<LimitExceededException>(() => orders.GetByIdsAsync([1, 2, 3, 4], new DetailsRequest(), Token));
            LimitExceededException extras = await Assert.ThrowsAsync<LimitExceededException>(() => orders.GetByIdsAsync(
                [1], new DetailsRequest { Include = ["totals", "raw", "names", "lineage", "permissions", "totals"] }, Token));
            BadRequestException unknown = await Assert.ThrowsAsync<BadRequestException>(() => orders.GetByIdsAsync([1], new DetailsRequest { Include = ["totals", "nope"] }, Token));

            Assert.Equal((LimitNames.MaxIds, 4, 3), (ids.Limit, ids.Actual, ids.Maximum));
            Assert.Equal((LimitNames.MaxExtras, 5, 4), (extras.Limit, extras.Actual, extras.Maximum));
            Assert.Equal("nope", unknown.Arguments!["name"]);
            Assert.Empty(_rig.Script.Batches);
        }

        // --- The extras ---

        [Fact]
        public async Task Each_requested_extra_is_produced_by_its_declaring_class_and_returned_in_request_order()
        {
            _rig.Grant((DetailsModel.Orders, AccessActions.Read, null));
            Script(main: [1, 2], orders: [Order(1, "A"), Order(2, "B")]);
            _ = _rig.Script.OnSql(static _ => new RawResult([QueryRig.Page(5).Rows]));

            await using AsyncServiceScope scope = _rig.Scope();
            DmOrderService orders = Orders(scope);
            EntitiesResult<DmOrder> result = await orders.GetByIdsAsync([2, 1, 2], new DetailsRequest { Include = ["lineage", "names", "totals", "raw"] }, Token);

            // --- Each producer was asked once over the distinct ids, the contributor for its own extra only ---
            DmOrderLineage lineage = Lineage(scope);
            Assert.Equal(["lineage"], Assert.Single(lineage.Requested));
            DetailsPlan<DmOrder, int> plan = Assert.Single(orders.Plans);
            Assert.Equal([2, 1], plan.Ids);
            Assert.Equal(_rig.Script.Batches[0].Of(ScriptedKind.Query).Single().Query<DmOrder>().Restrictions[0].TableSource, plan.IdsSource.Name);

            // --- The statements the extras appended joined the visible ids ---
            ScriptedBatch batch = Assert.Single(_rig.Script.Batches);
            ScriptedStatement raw = Assert.Single(batch.Of(ScriptedKind.Sql));
            Assert.Contains(plan.IdsSource.Name, raw.Text.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            ScriptedStatement names = Assert.Single(batch.Of(ScriptedKind.Rows), static statement => QueryRig.Render(statement.Spec.Filter) == DmOrderService.NamesFilter);

            // --- The values, in request order, serialised as the wire writes them ---
            Assert.Equal(["lineage", "names", "totals", "raw"], result.Extras!.Keys);
            Assert.Equal("[2,1]", result.Extras["lineage"].GetRawText());
            Assert.Equal("""["B","A"]""", result.Extras["totals"].GetRawText());
            Assert.Equal(JsonSerializer.Serialize(QueryRig.Page(5).Rows, TellmaJson.Options), result.Extras["raw"].GetRawText());
            Assert.NotNull(names.Spec);
            Assert.Equal(JsonValueKind.Object, result.Extras["names"].ValueKind);
        }

        [Theory]
        [InlineData("undeclared")]
        [InlineData("unrequested")]
        [InlineData("twice")]
        [InlineData("none")]
        [InlineData("writes")]
        [InlineData("no-result-set")]
        public async Task An_extra_added_against_the_name_rules_or_as_a_writing_statement_is_an_authoring_bug(string misbehaviour)
        {
            _rig.Grant((DetailsModel.Orders, AccessActions.Read, null));
            Script(main: [1], orders: [Order(1, "A")]);

            await using AsyncServiceScope scope = _rig.Scope();
            DmOrderService orders = Orders(scope);
            orders.Contribution = plan => Misbehave(misbehaviour, plan);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => orders.GetByIdAsync(1, new DetailsRequest { Include = [DmOrderService.Totals] }, Token));
            Assert.All(_rig.Script.Batches, static batch => Assert.False(batch.IsExecuted));
        }

        // --- The record permissions ---

        [Fact]
        public async Task The_permissions_refuse_what_a_filtered_grant_misses_then_what_the_rule_refuses_with_one_keyed_read_per_filtered_grant()
        {
            _rig.Grant(
                (DetailsModel.Orders, AccessActions.Read, null),
                (DetailsModel.Orders, AccessActions.Save, SaveFilter),
                (DetailsModel.Orders, AccessActions.Delete, null),
                (DetailsModel.Orders, AccessActions.Activate, null),
                (DetailsModel.Orders, "Post", PostFilter));
            Script(main: [1, 2], orders: [Order(1, "A"), Order(2, "P2")]);

            await using AsyncServiceScope scope = _rig.Scope();
            EntitiesResult<DmOrder> result = await Orders(scope).GetByIdsAsync([1, 2], new DetailsRequest { Include = ["permissions"] }, Token);

            // --- One keyed read per filtered grant, in the same round trip ---
            ScriptedBatch batch = Assert.Single(_rig.Script.Batches);
            string captured = $"@tb{batch.Of(ScriptedKind.Rows).Single(static statement => statement.Options is RowQueryOptions { CaptureKeys: true }).Ordinal}_keys";
            ScriptedStatement[] keyed = [.. batch.Of(ScriptedKind.Rows).Where(static statement => statement.Options is null && !statement.Spec.Aggregate)];
            Assert.Equal(2, keyed.Length);
            Assert.All(keyed, statement => Assert.Equal(("Id", "Id", captured), (statement.Spec.Select, statement.Spec.Restrictions[0].Path, statement.Spec.Restrictions[0].TableSource)));
            Assert.Contains(SaveFilter, QueryRig.Render(keyed[0].Spec.Filter), StringComparison.Ordinal);
            Assert.Contains(PostFilter, QueryRig.Render(keyed[1].Spec.Filter), StringComparison.Ordinal);

            // --- Outside the filter before the rule; a denied ability is the grant check's to show ---
            JsonElement permissions = Assert.Single(result.Extras!).Value;
            Assert.Equal(
                /*lang=json,strict*/ """[{"id":1,"refused":{}},{"id":2,"refused":{"save":"outside-filter","delete":"posted"}}]""",
                permissions.GetRawText());
        }

        // --- The rows of a tree by parent ---

        [Fact]
        public async Task The_roots_are_the_rows_without_a_parent_and_a_level_is_read_with_one_row_beyond_the_ceiling_in_key_order()
        {
            _rig.Grant((DetailsModel.Nodes, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(1, 2));

            await using AsyncServiceScope scope = _rig.Scope();
            QueryRowSet roots = await Nodes(scope).GetByParentIdsAsync(new ParentIdsRequest(null, null, null, null), Token);

            QuerySpec spec = Assert.Single(Assert.Single(_rig.Script.Batches).Of(ScriptedKind.Rows)).Spec;
            Assert.Equal(2, roots.RowCount);
            Assert.Contains("ParentId = null", QueryRig.Render(spec.Filter), StringComparison.Ordinal);
            Assert.Equal((0, 6, "Id"), (spec.Skip, spec.Take, spec.OrderBy));
            Assert.Empty(spec.Restrictions);
        }

        [Fact]
        public async Task The_children_of_parents_are_restricted_by_their_parent_key_and_the_roots_come_too_through_one_id_table()
        {
            _rig.Grant((DetailsModel.Nodes, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(3)).OnSql(static _ => RawResult.Empty);

            await using AsyncServiceScope scope = _rig.Scope();
            _ = await Nodes(scope).GetByParentIdsAsync(new ParentIdsRequest([4, 4, 5], null, null, null), Token);
            _ = await Nodes(scope).GetByParentIdsAsync(new ParentIdsRequest([4], null, null, null, IncludeRoots: true), Token);

            // --- The children: the parent key restricted on the distinct parents ---
            ScriptedStatement children = Assert.Single(_rig.Script.Batches[0].Of(ScriptedKind.Rows));
            Assert.Equal("ParentId", Assert.Single(children.Spec.Restrictions).Path);
            Assert.Equal([4, 5], children.KeySet<IdList>().Select(static row => row.Id));
            Assert.DoesNotContain("ParentId = null", QueryRig.Render(children.Spec.Filter), StringComparison.Ordinal);

            // --- With the roots: one key table filled from both seeks, the read restricted on it ---
            ScriptedBatch both = _rig.Script.Batches[1];
            ScriptedStatement table = Assert.Single(both.Of(ScriptedKind.DeclareIdTable));
            string text = Assert.Single(both.Of(ScriptedKind.Sql)).Text.Format;
            Assert.Contains("IS NULL", text, StringComparison.Ordinal);
            Assert.Contains("UNION", text, StringComparison.Ordinal);
            Assert.Equal(("Id", table.Name), (Assert.Single(Assert.Single(both.Of(ScriptedKind.Rows)).Spec.Restrictions).Path, both.Of(ScriptedKind.Rows)[0].Spec.Restrictions[0].TableSource));
            Assert.Equal(2, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task A_level_beyond_the_ceiling_or_too_many_parents_is_refused()
        {
            _rig.Grant((DetailsModel.Nodes, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(1, 2, 3, 4, 5, 6));

            await using AsyncServiceScope scope = _rig.Scope();
            LimitExceededException level = await Assert.ThrowsAsync<LimitExceededException>(() => Nodes(scope).GetByParentIdsAsync(new ParentIdsRequest([1], null, null, null), Token));
            LimitExceededException parents = await Assert.ThrowsAsync<LimitExceededException>(() => Nodes(scope).GetByParentIdsAsync(new ParentIdsRequest([1, 2, 3, 4], null, null, null), Token));

            Assert.Equal((LimitNames.MaxTake, 6, 5), (level.Limit, level.Actual, level.Maximum));
            Assert.Equal((LimitNames.MaxIds, 4, 3), (parents.Limit, parents.Actual, parents.Maximum));
            _ = Assert.Single(_rig.Script.Batches);
        }

        // --- Every row of a cacheable stack ---

        [Fact]
        public async Task A_warm_list_is_answered_with_no_round_trip_and_its_extras_ride_one_details_round_trip_over_its_rows()
        {
            _rig.Grant((DetailsModel.Customers, AccessActions.Read, null), (DetailsModel.Customers, AccessActions.Save, "Name != 'x'"));
            DmCustomer[] items = [Customer(1), Customer(2)];
            _cache.Result = new CacheResult<CachedEntitySet<DmCustomer>>(
                new CachedEntitySet<DmCustomer>(new VersionTag(Guid.Empty), QueryRig.Now, items, items.ToDictionary(static item => item.Id)),
                new VersionTag(Guid.Empty),
                CacheOutcome.Hit);
            _ = _rig.Script.OnRows(static statement => statement.Options is RowQueryOptions ? QueryRig.Page(1, 2) : QueryRig.Page(2));
            _ = _rig.Script.OnQuery<DmCustomer>(static _ => [Customer(1), Customer(2)]);

            await using AsyncServiceScope scope = _rig.Scope();
            EntityService<DmCustomer, int> customers = Customers(scope);
            EntitiesResult<DmCustomer> plain = await customers.GetAllCachedAsync(new DetailsRequest(), Token);
            Assert.Empty(_rig.Script.Batches);
            EntitiesResult<DmCustomer> permitted = await customers.GetAllCachedAsync(new DetailsRequest { Include = ["permissions"] }, Token);

            Assert.Same(items, plain.Entities);
            Assert.Equal([1L, 2L], plain.Ids);
            Assert.Equal(1, _rig.Script.RoundTrips);
            ScriptedStatement main = Assert.Single(_rig.Script.Batches[0].Of(ScriptedKind.Rows), static statement => statement.Options is RowQueryOptions);
            Assert.Equal([1, 2], main.KeySet<IdList>().Select(static row => row.Id));
            Assert.Equal([1L, 2L], permitted.Ids);
            Assert.Equal(/*lang=json,strict*/ """[{"id":1,"refused":{"save":"outside-filter"}},{"id":2,"refused":{}}]""", permitted.Extras!["permissions"].GetRawText());
        }

        [Fact]
        public async Task An_oversized_list_is_read_whole_in_key_order_on_every_call()
        {
            _rig.Grant((DetailsModel.Customers, AccessActions.Read, null));
            _cache.Result = new CacheResult<CachedEntitySet<DmCustomer>>(null, new VersionTag(Guid.Empty), CacheOutcome.Oversized);
            _ = _rig.Script.OnQuery<DmCustomer>(static _ => [Customer(1), Customer(2), Customer(3)]);

            await using AsyncServiceScope scope = _rig.Scope();
            EntitiesResult<DmCustomer> all = await Customers(scope).GetAllCachedAsync(new DetailsRequest(), Token);
            _ = await Customers(scope).GetAllCachedAsync(new DetailsRequest(), Token);

            Assert.Equal([1L, 2L, 3L], all.Ids);
            Assert.Equal(2, _rig.Script.RoundTrips);
            ScriptedStatement whole = Assert.Single(_rig.Script.Batches[0].Statements);
            EntityQuery<DmCustomer> query = whole.Query<DmCustomer>();
            Assert.Equal(("Id", true), (query.OrderBy, query.AllProperties));
            Assert.Empty(query.Restrictions);
        }

        // --- Helpers ---

        /// <summary>Breaks one of the rules a producer is held to.</summary>
        /// <param name="misbehaviour">Which rule.</param>
        /// <param name="plan">The plan.</param>
        private static void Misbehave(string misbehaviour, DetailsPlan<DmOrder, int> plan)
        {
            switch (misbehaviour)
            {
                case "undeclared":
                    plan.AddComputedExtra(DmOrderLineage.Lineage, static rows => rows.Count);
                    break;
                case "unrequested":
                    plan.AddComputedExtra(DmOrderService.Raw, static rows => rows.Count);
                    break;
                case "twice":
                    plan.AddComputedExtra(DmOrderService.Totals, static rows => rows.Count);
                    plan.AddComputedExtra(DmOrderService.Totals, static rows => rows.Count);
                    break;
                case "writes":
                    _ = plan.AddRead($"DELETE FROM [dm].[Orders]", new SqlOptions(writes: new HashSet<TableName> { new("dm", "Orders") }));
                    break;
                case "no-result-set":
                    plan.AddExtra(DmOrderService.Totals, $"SELECT 1", SqlOptions.None);
                    break;
                default:
                    // Adds nothing, though the request includes the extra the service declares.
                    break;
            }
        }

        /// <summary>Answers the plan's reads: the main ids, the rows, and the lines' targets, customers 7 and 8 twice over.</summary>
        /// <param name="main">The visible ids the main read admits, in its order.</param>
        /// <param name="orders">The rows the entity query reads.</param>
        private void Script(int[] main, DmOrder[] orders)
        {
            _ = _rig.Script
                .OnRows(statement => statement.Options is RowQueryOptions { CaptureKeys: true } ? QueryRig.Page(main)
                    : statement.Spec.Aggregate ? new RowPage(PathRows(["ShipTo.Id"], [7], [8], [7]), null, null, null)
                    : QueryRig.Render(statement.Spec.Filter).Contains(SaveFilter, StringComparison.Ordinal) ? QueryRig.Page(1)
                    : QueryRig.Page(main))
                .OnQuery(_ => orders);
        }

        /// <summary>An order.</summary>
        /// <param name="id">The id.</param>
        /// <param name="code">The code.</param>
        /// <returns>The order.</returns>
        private static DmOrder Order(int id, string code)
        {
            return new DmOrder { Id = id, Code = code, Name = code, Lines = [] };
        }

        /// <summary>A customer.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The customer.</returns>
        private static DmCustomer Customer(int id)
        {
            return new DmCustomer { Id = id, Name = "C" + id };
        }

        /// <summary>A row set of integer columns whose names are dotted paths, as a related read returns.</summary>
        /// <param name="columns">The columns' paths.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The row set.</returns>
        private static QueryRowSet PathRows(string[] columns, params int?[][] rows)
        {
            using DataTable table = new();
            foreach (string column in columns)
            {
                _ = table.Columns.Add(column, typeof(int));
            }

            foreach (int?[] row in rows)
            {
                _ = table.Rows.Add([.. row.Select(static value => value is int number ? (object)number : DBNull.Value)]);
            }

            using DbDataReader reader = table.CreateDataReader();
            QueryRowSet.Builder builder = new(
                [.. columns.Select(static name => new QueryColumn(name, QueryexType.QxNumeric, null, null, Nullable: true, name.Split('.'), GroupingKey: true))],
                [.. columns.Select(static _ => typeof(int))],
                rows.Length);
            while (reader.Read())
            {
                builder.AppendRow(reader);
            }

            return builder.Build();
        }

        /// <summary>A scope's order service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static DmOrderService Orders(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<DmOrderService>();
        }

        /// <summary>A scope's order lineage contributor.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The contributor.</returns>
        private static DmOrderLineage Lineage(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetServices<IDetailsContributor<DmOrder>>().OfType<DmOrderLineage>().Single();
        }

        /// <summary>A scope's customer service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<DmCustomer, int> Customers(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<DmCustomer, int>>();
        }

        /// <summary>A scope's node service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<DmNode, int> Nodes(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<DmNode, int>>();
        }

        /// <summary>A cache whose list of customers a test sets.</summary>
        private sealed class FakeCache : ICacheableEntities
        {
            /// <summary>What the cache answers for the customers.</summary>
            public CacheResult<CachedEntitySet<DmCustomer>>? Result { get; set; }

            /// <inheritdoc />
            public Task<CacheResult<CachedEntitySet<TEntity>>> GetAsync<TEntity>()
                where TEntity : class
            {
                return Task.FromResult((CacheResult<CachedEntitySet<TEntity>>)(object)(Result ?? throw new InvalidOperationException("The test set no cached list.")));
            }

            /// <inheritdoc />
            public CachedEntitySet<TEntity>? Peek<TEntity>()
                where TEntity : class
            {
                return null;
            }

            /// <inheritdoc />
            public Task<IReadOnlyDictionary<string, string>> GetWireTagsAsync()
            {
                return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.Ordinal));
            }
        }
    }
}
