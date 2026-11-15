// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Scripted;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Crud.Validation
{
    /// <summary>
    ///     The validation context loader over a scripted batch: requests merged by structural key,
    ///     key sets and selects unioned, absences and earlier rounds cached, payload rows primed,
    ///     visibility conjoined, and the raw read held to no writes.
    /// </summary>
    public sealed class ContextLoaderTests : IDisposable
    {
        /// <summary>Writes a raw read declares where the analyzer cannot see them, so only the run-time check refuses them.</summary>
        private static readonly SqlOptions DeclaredWrites = new(writes: new HashSet<TableName> { new("vm", "Widgets") });

        /// <summary>The composed validation model.</summary>
        private readonly ValidationRig _rig = new();

        /// <summary>The ids no widget has in the scripted database.</summary>
        private readonly HashSet<int> _missing = [];

        /// <inheritdoc />
        public void Dispose()
        {
            _rig.Dispose();
        }

        // ------------------------------------------------------------------
        // Keyed loads
        // ------------------------------------------------------------------

        /// <summary>Keyed requests of one round ride one statement over the union of their keys and selects.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task Keyed_requests_of_a_round_share_one_statement_over_the_union_of_their_keys_and_selects()
        {
            BatchScript script = WidgetScript();
            _ = _missing.Add(3);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            ContextRef<IReadOnlyDictionary<int, VmWidget>>? first = null;
            ContextRef<IReadOnlyDictionary<int, VmWidget>>? second = null;

            scheduler.Run("first", async () =>
            {
                first = loader.ByIds<VmWidget, int>([1, 2], "Code");
                await loader.LoadAsync();
            });
            scheduler.Run("second", async () =>
            {
                second = loader.ByIds<VmWidget, int>([2, 3, 2], "name");
                await loader.LoadAsync();
            });

            Assert.Equal(1, await scheduler.RunAsync());

            // --- One batch, one key-set table, one query under both selects and the key ---
            ScriptedBatch batch = Assert.Single(script.Batches);
            ScriptedStatement query = Assert.Single(batch.Of(ScriptedKind.Query));
            Assert.Equal([1, 2, 3], query.KeySet<IdList>().Select(static row => row.Id));
            Assert.Equal("Code, Id, name", query.Query<VmWidget>().Select);
            Assert.False(query.Query<VmWidget>().AllProperties);
            KeySetRestriction restriction = Assert.Single(query.Query<VmWidget>().Restrictions);
            Assert.Equal("Id", restriction.Path);
            Assert.Null(query.Query<VmWidget>().Filter);

            // --- Each request sees its own keys; the missing key is absent ---
            Assert.Equal([1, 2], first!.Value.Keys.Order());
            Assert.Equal([2], second!.Value.Keys);
            Assert.Same(first.Value[2], second.Value[2]);
        }

        /// <summary>A key a round found missing, and a key read under a covering select, are answered at once without a statement.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task Absences_and_covered_keys_are_answered_from_the_cache()
        {
            BatchScript script = WidgetScript();
            _ = _missing.Add(9);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            List<bool> loadedAtOnce = [];

            scheduler.Run("validator", async () =>
            {
                _ = loader.ByIds<VmWidget, int>([4, 9], select: null);
                await loader.LoadAsync();

                // Every property of 4 was read and 9 found nothing: both answer at once, under any select.
                ContextRef<IReadOnlyDictionary<int, VmWidget>> again = loader.ByIds<VmWidget, int>([9, 4], "Code");
                loadedAtOnce.Add(again.IsLoaded);
                loadedAtOnce.Add(loader.LoadAsync().IsCompleted);
                Assert.Equal([4], again.Value.Keys);
            });

            Assert.Equal(1, await scheduler.RunAsync());
            Assert.Equal([true, true], loadedAtOnce);
            Assert.Equal(1, script.RoundTrips);
            Assert.True(Assert.Single(script.Batches[0].Of(ScriptedKind.Query)).Query<VmWidget>().AllProperties);
        }

        /// <summary>A key read again under a wider select is read under the union, so what the cache holds only grows.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_wider_select_reads_again_under_the_union()
        {
            BatchScript script = WidgetScript();
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);

            scheduler.Run("validator", async () =>
            {
                _ = loader.ByIds<VmWidget, int>([1], "Code");
                await loader.LoadAsync();
                _ = loader.ByIds<VmWidget, int>([1], "Price");
                await loader.LoadAsync();
                Assert.True(loader.ByIds<VmWidget, int>([1], "Code, Price").IsLoaded);
            });

            Assert.Equal(2, await scheduler.RunAsync());
            Assert.Equal(
                ["Code, Id", "Code, Id, Price"],
                script.Batches.Select(static batch => Assert.Single(batch.Of(ScriptedKind.Query)).Query<VmWidget>().Select));
        }

        /// <summary>A primed payload row answers every select at once and is never replaced by a read.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_primed_row_answers_at_once_and_wins_over_the_database()
        {
            BatchScript script = WidgetScript();
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            VmWidget payload = new() { Id = 5, Code = "PAYLOAD" };
            loader.Prime<VmWidget, int>(payload);
            ContextRef<IReadOnlyDictionary<int, VmWidget>>? mixed = null;

            scheduler.Run("validator", async () =>
            {
                ContextRef<IReadOnlyDictionary<int, VmWidget>> primed = loader.ByIds<VmWidget, int>([5], "Code, Name");
                Assert.True(primed.IsLoaded);
                Assert.Same(payload, primed.Value[5]);

                mixed = loader.ByIds<VmWidget, int>([5, 6], select: null);
                await loader.LoadAsync();
            });

            Assert.Equal(1, await scheduler.RunAsync());
            Assert.Equal([6], Assert.Single(script.Batches[0].Of(ScriptedKind.Query)).KeySet<IdList>().Select(static row => row.Id));
            Assert.Same(payload, mixed!.Value[5]);
            Assert.Equal("W6", mixed.Value[6].Code);
        }

        /// <summary>A unique string key is read through a string key set and compared ignoring case, as the collation does.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_unique_string_key_loads_through_a_string_key_set_and_ignores_case()
        {
            BatchScript script = new BatchScript().OnQuery<VmWidget>(static statement =>
                [.. statement.KeySet<StringList>().Where(static row => row.Id != "zz").Select(static row => new VmWidget { Id = 1, Code = row.Id.ToUpperInvariant() })]);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            ContextRef<IReadOnlyDictionary<string, VmWidget>>? byCode = null;

            scheduler.Run("validator", async () =>
            {
                byCode = loader.ByKey<VmWidget, string>("code", ["ab", "AB", "zz"], "Name");
                await loader.LoadAsync();
            });

            _ = await scheduler.RunAsync();
            ScriptedStatement query = Assert.Single(script.Batches[0].Of(ScriptedKind.Query));
            Assert.Equal(["ab", "zz"], query.KeySet<StringList>().Select(static row => row.Id));
            Assert.Equal("Code", Assert.Single(query.Query<VmWidget>().Restrictions).Path);
            Assert.Equal("Code, Name", query.Query<VmWidget>().Select);
            Assert.Equal("AB", byCode!.Value["ab"].Code);
            Assert.False(byCode.Value.ContainsKey("zz"));
        }

        /// <summary>
        ///     String keys meet as the platform collation meets them: width, trailing spaces and the
        ///     sharp s fold, so the key-set table never holds two keys its primary key equates, and a
        ///     row the database matches answers the key that was asked for; accents still differ.
        /// </summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task String_keys_meet_as_the_collation_meets_them()
        {
            // The database answers with its own spelling of each key it matched.
            Dictionary<string, string> stored = new(StringComparer.Ordinal) { ["A1"] = "A1", ["X"] = "X", ["straße"] = "STRASSE", ["é"] = "É" };
            BatchScript script = new BatchScript().OnQuery<VmWidget>(statement =>
                [.. statement.KeySet<StringList>().Select(static row => row.Id).Where(stored.ContainsKey).Select(id => new VmWidget { Id = 1, Code = stored[id] })]);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            ContextRef<IReadOnlyDictionary<string, VmWidget>>? byCode = null;

            scheduler.Run("validator", async () =>
            {
                byCode = loader.ByKey<VmWidget, string>("Code", ["A1", "Ａ1", "X", "X ", "straße", "é", "e"]);
                await loader.LoadAsync();
            });

            _ = await scheduler.RunAsync();
            ScriptedStatement query = Assert.Single(script.Batches[0].Of(ScriptedKind.Query));
            Assert.Equal(["A1", "X", "straße", "é", "e"], query.KeySet<StringList>().Select(static row => row.Id));
            Assert.Equal("A1", byCode!.Value["Ａ1"].Code);
            Assert.Equal("X", byCode.Value["X "].Code);
            Assert.Equal("STRASSE", byCode.Value["straße"].Code);
            Assert.Equal("É", byCode.Value["é"].Code);
            Assert.False(byCode.Value.ContainsKey("e"));
        }

        /// <summary>A key that is not unique on its own, a key of the wrong type, and a null key are refused.</summary>
        [Fact]
        public void Keys_that_do_not_key_one_row_are_refused()
        {
            (_, ContextLoader loader) = _rig.Loader(new BatchScript());

            Assert.Contains("not unique on its own", Assert.Throws<ArgumentException>(() => loader.ByKey<VmWidget, string>("Name", ["x"])).Message, StringComparison.Ordinal);
            _ = Assert.Throws<ArgumentException>(() => loader.ByKey<VmWidget, string>("Nothing", ["x"]));
            _ = Assert.Throws<ArgumentException>(() => loader.ByIds<VmWidget, long>([1L]));
            _ = Assert.Throws<ArgumentException>(() => loader.ByKey<VmWidget, string>("Code", [null!]));
            _ = Assert.Throws<ArgumentException>(() => loader.ByIds<VmWidget, int>([1], " , "));
            _ = Assert.Throws<ArgumentException>(() => loader.Ancestors<VmWidget, int>([1]));
            _ = Assert.Throws<ArgumentException>(() => loader.ByParentIds<VmWidget, int>([1]));
            Assert.False(loader.HasPending);
        }

        /// <summary>Children load by parent id, and every requested parent is answered, one without children by an empty list.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task Children_answer_every_requested_parent()
        {
            BatchScript script = new BatchScript().OnQuery<VmWidgetLine>(static statement =>
                [.. statement.KeySet<IdList>().Where(static row => row.Id == 1).SelectMany(static row => new[] { new VmWidgetLine { Id = 11, WidgetId = 1 }, new VmWidgetLine { Id = 12, WidgetId = 1 } })]);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            ContextRef<IReadOnlyDictionary<int, IReadOnlyList<VmWidgetLine>>>? lines = null;

            scheduler.Run("validator", async () =>
            {
                lines = loader.ByParentIds<VmWidgetLine, int>([1, 2], "Units");
                await loader.LoadAsync();
                Assert.True(loader.ByParentIds<VmWidgetLine, int>([2], "Units").IsLoaded);
            });

            _ = await scheduler.RunAsync();
            ScriptedStatement query = Assert.Single(script.Batches[0].Of(ScriptedKind.Query));
            Assert.Equal("WidgetId", Assert.Single(query.Query<VmWidgetLine>().Restrictions).Path);
            Assert.Equal("Units, WidgetId", query.Query<VmWidgetLine>().Select);
            Assert.Equal([11, 12], lines!.Value[1].Select(static line => line.Id));
            Assert.Empty(lines.Value[2]);
        }

        // ------------------------------------------------------------------
        // Compiled loads
        // ------------------------------------------------------------------

        /// <summary>Queries merge by filter, under the union of their selects; another filter is another statement.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task Queries_merge_by_filter_under_the_union_of_their_selects()
        {
            BatchScript script = new BatchScript().OnQuery<VmWidget>(static _ => [new VmWidget { Id = 1, Code = "A" }]);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            List<ContextRef<IReadOnlyList<VmWidget>>> refs = [];

            scheduler.Run("validator", async () =>
            {
                refs.Add(loader.Query<VmWidget>(FilterTree.Leaf("Quantity > 3"), select: "Code"));
                refs.Add(loader.Query<VmWidget>(FilterTree.Leaf("Quantity > 3"), select: "Name"));
                refs.Add(loader.Query<VmWidget>(FilterTree.Leaf("Quantity > 4"), select: "Name"));
                await loader.LoadAsync();

                // The same query under a covered select answers at once.
                refs.Add(loader.Query<VmWidget>(FilterTree.Leaf("Quantity > 3"), select: "Name"));
                Assert.True(refs[^1].IsLoaded);
            });

            _ = await scheduler.RunAsync();
            IReadOnlyList<ScriptedStatement> queries = script.Batches[0].Of(ScriptedKind.Query);
            Assert.Equal(["Code, Name", "Name"], queries.Select(static query => query.Query<VmWidget>().Select));
            Assert.All(queries, static query => Assert.Equal("Id", query.Query<VmWidget>().OrderBy));
            Assert.Same(refs[0].Value, refs[1].Value);
            Assert.Same(refs[0].Value, refs[3].Value);
        }

        /// <summary>A visible load reads nothing under a denial, conjoins a filtered grant, and needs a read decision.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_visible_load_conjoins_the_read_decision()
        {
            var grant = FilterTree.Leaf("Code <> 'X'");
            BatchScript script = new BatchScript().OnQuery<VmWidget>(static _ => []);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(
                script,
                readDecisions: entity => entity == typeof(VmWidget)
                    ? new AccessDecision("vm.VmWidget", "Read", AccessOutcome.Filtered, grant, [], [])
                    : AccessDecision.Deny("vm.VmNode", "Read"));
            var filter = FilterTree.Leaf("Quantity > 3");

            scheduler.Run("validator", async () =>
            {
                Assert.True(loader.Visible<VmNode>(filter).IsLoaded);
                Assert.Empty(loader.Visible<VmNode>(filter).Value);
                _ = loader.Visible<VmWidget>(filter, select: "Code");
                await loader.LoadAsync();
            });

            _ = await scheduler.RunAsync();
            FilterTree.AndNode conjoined = Assert.IsType<FilterTree.AndNode>(Assert.Single(script.Batches[0].Of(ScriptedKind.Query)).Query<VmWidget>().Filter);
            Assert.Equal([filter, grant], conjoined.Children);

            (_, ContextLoader blind) = _rig.Loader(new BatchScript());
            _ = Assert.Throws<InvalidOperationException>(() => blind.Visible<VmWidget>(filter));
        }

        /// <summary>An existence is a count capped at zero, so it shares the count's statement; another cap is another statement.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task An_existence_shares_the_count_capped_at_zero()
        {
            BatchScript script = new BatchScript().OnCount(static statement => statement.Cap == 0 ? 1 : 7);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            string root = _rig.Of<VmWidget>().Name;
            var filter = FilterTree.Leaf("Quantity > 3");
            ContextRef<bool>? exists = null;
            ContextRef<int>? zero = null;
            ContextRef<int>? capped = null;

            scheduler.Run("validator", async () =>
            {
                exists = loader.Exists(root, filter);
                zero = loader.Count(root, filter, cap: 0);
                capped = loader.Count(root, filter, cap: 5);
                await loader.LoadAsync();
            });

            _ = await scheduler.RunAsync();
            IReadOnlyList<ScriptedStatement> counts = script.Batches[0].Of(ScriptedKind.Count);
            Assert.Equal([0, 5], counts.Select(static count => count.Cap));
            Assert.Same(filter, counts[0].Spec.Filter);
            Assert.True(exists!.Value);
            Assert.Equal(1, zero!.Value);
            Assert.Equal(7, capped!.Value);
            _ = Assert.Throws<ArgumentException>(() => loader.Count("vm.Nothing", filter, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => loader.Count(root, filter, -1));
        }

        /// <summary>Rows queries merge by structure and are cached across rounds.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task Rows_queries_merge_and_are_cached()
        {
            QueryRowSet rows = ValidationRig.IntRows(["Id"], [1], [2]);
            BatchScript script = new BatchScript().OnRows(_ => new RowPage(rows, null, null));
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            QuerySpec Spec()
            {
                return new QuerySpec { Root = _rig.Map.Descriptor(_rig.Of<VmWidget>()), Select = "Id", Filter = FilterTree.Leaf("Quantity > 3") };
            }

            scheduler.Run("validator", async () =>
            {
                ContextRef<QueryRowSet> first = loader.Rows(Spec());
                ContextRef<QueryRowSet> second = loader.Rows(Spec());
                await loader.LoadAsync();
                Assert.Same(rows, first.Value);
                Assert.Same(rows, second.Value);
                Assert.True(loader.Rows(Spec()).IsLoaded);
            });

            Assert.Equal(1, await scheduler.RunAsync());
            Assert.Single(script.Batches[0].Of(ScriptedKind.Rows));
        }

        /// <summary>Ancestors load each id's chain in one rows query, nearest first; a missing id is absent and later chains answer from the cache.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task Ancestors_answer_each_chain_nearest_first()
        {
            BatchScript script = new BatchScript().OnRows(static _ => new RowPage(
                ValidationRig.IntRows(["Id", "ParentId"], [3, 2]),
                null,
                ValidationRig.IntRows(["Id", "ParentId"], [2, 1], [1, null])));
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            ContextRef<IReadOnlyDictionary<int, IReadOnlyList<int>>>? chains = null;

            scheduler.Run("validator", async () =>
            {
                chains = loader.Ancestors<VmNode, int>([3, 9]);
                await loader.LoadAsync();
                ContextRef<IReadOnlyDictionary<int, IReadOnlyList<int>>> cached = loader.Ancestors<VmNode, int>([2, 1, 9]);
                Assert.True(cached.IsLoaded);
                Assert.Equal([1], cached.Value[2]);
                Assert.Empty(cached.Value[1]);
            });

            _ = await scheduler.RunAsync();
            ScriptedStatement statement = Assert.Single(script.Batches[0].Of(ScriptedKind.Rows));
            Assert.Equal("Id, ParentId", statement.Spec.Select);
            Assert.True(Assert.IsType<RowQueryOptions>(statement.Options).IncludeAncestors);
            Assert.Equal([3, 9], statement.KeySet<IdList>().Select(static row => row.Id));
            Assert.Equal([2, 1], chains!.Value[3]);
            Assert.False(chains.Value.ContainsKey(9));
        }

        /// <summary>A raw read is merged with an identical one, and refuses declared writes.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_raw_read_merges_identical_text_and_refuses_declared_writes()
        {
            RawResult raw = new([ValidationRig.IntRows(["Id"], [1])]);
            BatchScript script = new BatchScript().OnSql(_ => raw);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);

            scheduler.Run("validator", async () =>
            {
                int one = 1;
                int two = 2;
                ContextRef<RawResult> first = loader.Sql($"SELECT [Id] FROM [vm].[Widgets] WHERE [Id] = {one};", new SqlOptions(resultSets: 1));
                ContextRef<RawResult> second = loader.Sql($"SELECT [Id] FROM [vm].[Widgets] WHERE [Id] = {one};", new SqlOptions(resultSets: 1));
                ContextRef<RawResult> third = loader.Sql($"SELECT [Id] FROM [vm].[Widgets] WHERE [Id] = {two};", new SqlOptions(resultSets: 1));
                await loader.LoadAsync();
                Assert.Same(raw, first.Value);
                Assert.Same(raw, second.Value);
                Assert.Same(raw, third.Value);
            });

            _ = await scheduler.RunAsync();
            Assert.Equal(2, script.Batches[0].Of(ScriptedKind.Sql).Count);
            Assert.Empty(script.Batches[0].WrittenTables);
            _ = Assert.Throws<ArgumentException>(() => loader.Sql($"SELECT 1;", DeclaredWrites));
        }

        // ------------------------------------------------------------------
        // Rounds
        // ------------------------------------------------------------------

        /// <summary>A round with nothing pending sends no batch; a request made during a dispatch waits for the next round.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task An_empty_round_sends_nothing_and_a_late_request_waits_for_the_next()
        {
            BatchScript script = WidgetScript();
            (_, ContextLoader loader) = _rig.Loader(script);
            ScriptedBatch empty = script.NewBatch();

            await loader.DispatchAsync(empty, TestContext.Current.CancellationToken);
            Assert.False(empty.IsExecuted);

            ContextRef<IReadOnlyDictionary<int, VmWidget>> early = loader.ByIds<VmWidget, int>([1]);
            ContextLoader.ContextRound round = loader.BeginRound();
            ContextRef<IReadOnlyDictionary<int, VmWidget>> late = loader.ByIds<VmWidget, int>([2]);
            ScriptedBatch batch = script.NewBatch();
            round.Compose(batch);
            _ = await batch.ExecuteAsync(TestContext.Current.CancellationToken);
            round.Complete();

            Assert.True(early.IsLoaded);
            Assert.False(late.IsLoaded);
            Assert.True(loader.HasPending);
            _ = Assert.Throws<InvalidOperationException>(() => loader.BeginRound().Complete());
        }

        /// <summary>A round's statements compile on a real batch beside whatever else it carries.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_rounds_statements_compile_on_a_real_batch()
        {
            (_, ContextLoader loader) = _rig.Loader(new BatchScript(), readDecisions: static _ => new AccessDecision("vm.VmWidget", "Read", AccessOutcome.Unrestricted, null, [], []));
            string root = _rig.Of<VmWidget>().Name;
            _ = loader.ByIds<VmWidget, int>([1, 2], "Code");
            _ = loader.ByKey<VmWidget, string>("Code", ["A"], select: null);
            _ = loader.ByParentIds<VmWidgetLine, int>([1], "Units");
            _ = loader.Query<VmWidget>(FilterTree.Leaf("Quantity > 3"), select: "Name");
            _ = loader.Visible<VmWidget>(FilterTree.Leaf("Quantity > 4"));
            _ = loader.Exists(root, FilterTree.Leaf("Quantity > 5"));
            _ = loader.Ancestors<VmNode, int>([1]);
            _ = loader.Sql($"SELECT [Id] FROM [vm].[Widgets];", new SqlOptions(resultSets: 1));

            DataBatch batch = _rig.Harness.Batch(BatchPurpose.Validate);
            loader.BeginRound().Compose(batch);
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.Contains("[vm].[Widgets]", assembly.Text, StringComparison.Ordinal);
            Assert.Contains("[vm].[WidgetLines]", assembly.Text, StringComparison.Ordinal);
            Assert.Contains("[vm].[Nodes]", assembly.Text, StringComparison.Ordinal);
        }

        /// <summary>A script answering widget queries by their key set, leaving out the ids marked missing.</summary>
        /// <returns>The script.</returns>
        private BatchScript WidgetScript()
        {
            return new BatchScript().OnQuery<VmWidget>(statement =>
                [.. statement.KeySet<IdList>()
                    .Where(row => !_missing.Contains(row.Id))
                    .Select(static row => new VmWidget { Id = row.Id, Code = string.Create(CultureInfo.InvariantCulture, $"W{row.Id}") })]);
        }
    }
}
