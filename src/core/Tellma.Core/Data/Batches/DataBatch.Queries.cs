// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Batches
{
    // The query statements of spec 0012 §5.2, §5.4 and §11.3–§11.5.
    /// <content>The query statements: rows pages, counts, constants, assertions and entity queries.</content>
    internal sealed partial class DataBatch
    {
        /// <summary>The keyword a key-capture or ancestor table's key column is declared with, by the key's store family.</summary>
        private static readonly FrozenDictionary<QueryexStoreFamily, string> KeyTypes = new Dictionary<QueryexStoreFamily, string>
        {
            [QueryexStoreFamily.QxInt] = "int",
            [QueryexStoreFamily.QxBigInt] = "bigint",
        }.ToFrozenDictionary();

        /// <summary>No declared parameter values.</summary>
        private static readonly IReadOnlyDictionary<string, object?> NoValues = FrozenDictionary<string, object?>.Empty;

        /// <summary>The query statements that may be shared, by structural key; cleared at each declared write.</summary>
        private readonly Dictionary<QueryStatementKey, object> _sharedQueries = [];

        /// <summary>The key-capture table of each handle whose statement captured its keys.</summary>
        private readonly Dictionary<object, SqlIdentifier> _capturedKeys = new(ReferenceEqualityComparer.Instance);

        /// <summary>The tenant's query schema and its map, read once per batch.</summary>
        private QueryexSchemaMap Map => field ??= _executor.Queries.Schemas.Map(Connection.Shape);

        /// <summary>The context values every query statement of the batch binds, read once so they all see one today().</summary>
        private QueryexContextValues Context => field ??= Connection.Context;

        /// <inheritdoc />
        public BatchResult<RowPage> Rows(QuerySpec spec, QueryArguments? arguments, RowQueryOptions? options)
        {
            ArgumentNullException.ThrowIfNull(spec);
            options ??= new RowQueryOptions();
            EnsureComposing();
            EnsurePrologue();

            // --- An identical statement already composed in this write epoch answers ---
            QueryStatementKey key = QueryStatementKey.For("rows").Add(spec).Add(arguments)
                .Add(options.CountCap).Add(options.IncludeAncestors).Add(options.AncestorsFilter).Add(options.CaptureKeys)
                .Build(Limits);
            if (_sharedQueries.TryGetValue(key, out object? shared))
            {
                return (BatchResult<RowPage>)shared;
            }

            // --- Refusals: a grouped page has no keys to capture or climb from; its count counts its groups ---
            bool capture = options.CaptureKeys || options.IncludeAncestors;
            if (spec.Aggregate && capture)
            {
                throw new ArgumentException(
                    "A grouped rows query neither captures keys nor includes ancestors; those apply to rows of an entity.", nameof(options));
            }

            int? countTake = options.CountCap is int cap ? CountTake(cap, nameof(options)) : null;
            Type entityType = Map.Metadata(spec.Root).ClrType;

            // --- The plan: [keys twin], page, [count], [ancestors] ---
            QueryPlan plan = new(this);
            RowsState state = new();
            SqlIdentifier? keys = capture ? PlanKeys(plan, spec, arguments, entityType) : null;
            QuerySpec page = keys is SqlIdentifier captured ? Restricted(spec, captured) : spec;
            PlanRead(plan, page, arguments, entityType, rows => state.Rows = rows);
            if (countTake is int take)
            {
                PlanCount(plan, spec, arguments, take, entityType, count => state.Count = count);
            }

            if (options.IncludeAncestors)
            {
                PlanAncestors(plan, spec with { Filter = options.AncestorsFilter }, arguments, keys!, entityType, rows => state.Ancestors = rows);
            }

            BatchResult<RowPage> result = new();
            plan.Append(cursor => cursor.Complete(result, new RowPage(state.Rows!, state.Count, state.Ancestors)));

            _sharedQueries[key] = result;
            if (keys is SqlIdentifier remembered)
            {
                _capturedKeys[result] = remembered;
            }

            return result;
        }

        /// <inheritdoc />
        public BatchResult<int> Count(QuerySpec spec, QueryArguments? arguments, int cap = 10000)
        {
            ArgumentNullException.ThrowIfNull(spec);
            int take = CountTake(cap, nameof(cap));
            if (spec.Aggregate)
            {
                throw new ArgumentException("A count counts the rows of an entity, so its spec does not group.", nameof(spec));
            }

            EnsureComposing();
            EnsurePrologue();

            QueryStatementKey key = QueryStatementKey.For("count").Add(spec).Add(arguments).Add(cap).Build(Limits);
            if (_sharedQueries.TryGetValue(key, out object? shared))
            {
                return (BatchResult<int>)shared;
            }

            QueryPlan plan = new(this);
            int counted = 0;
            PlanCount(plan, spec, arguments, take, Map.Metadata(spec.Root).ClrType, count => counted = count);
            BatchResult<int> result = new();
            plan.Append(cursor => cursor.Complete(result, counted));

            _sharedQueries[key] = result;
            return result;
        }

        /// <inheritdoc />
        public BatchResult<QueryRowSet> EvaluateConstant(IReadOnlyList<ConstantItem> items, int queryexVersion)
        {
            ArgumentNullException.ThrowIfNull(items);
            EnsureComposing();
            EnsurePrologue();

            QueryPlan plan = new(this);
            int ordinal = plan.NextOrdinal;
            ConstantCompilationOptions options = new()
            {
                Schema = Map.Schema,
                QueryexVersion = queryexVersion,
                HasUser = Context.UserId is not null,
                BatchOrdinal = ordinal,
                Limits = Limits,
            };
            QueryexResult<CompiledQuery> compiled = _executor.Queries.Engine.EvaluateConstant(items, options);
            CompiledQuery statement = compiled.Value ?? throw Refused("The constant items", compiled.Diagnostics, nameof(items));

            QueryRowSet? row = null;
            plan.Add(new PlannedStatement(ordinal, statement.Sql, Slots(statement, arguments: null), Reader: Read(ordinal, statement.Columns, rows => row = rows)));
            BatchResult<QueryRowSet> result = new();
            plan.Append(cursor => cursor.Complete(result, row!));
            return result;
        }

        /// <inheritdoc />
        public void Assert(QuerySpec countSpec, QueryArguments? arguments, int expected, int errorNumber, string code)
        {
            ArgumentNullException.ThrowIfNull(countSpec);
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            if (!TellmaSqlErrors.IsPlatform(errorNumber) && !TellmaSqlErrors.IsDistribution(errorNumber))
            {
                throw new ArgumentOutOfRangeException(nameof(errorNumber), errorNumber, "An assertion throws a platform error number or one in the distribution band.");
            }

            // The code reaches the text as a literal, so it is held to the shape of an error code.
            if (!code.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            {
                throw new ArgumentException($"The error code '{code}' is not letters, digits, dots, underscores and hyphens.", nameof(code));
            }

            EnsureComposing();
            EnsurePrologue();

            // --- The count's body: an unpaged ordering has no meaning in a count, and no place in a derived table ---
            QueryPlan plan = new(this);
            int ordinal = plan.NextOrdinal;
            QuerySpec counted = Paged(countSpec) ? countSpec : countSpec with { OrderBy = null };
            CompiledQuery compiled = Compile(counted, arguments, ordinal);

            string expectedName = ReservedSqlNames.Statement(ordinal, ReservedSqlNames.StatementNames.Scalar(0));
            string thrown = string.Create(CultureInfo.InvariantCulture, $"THROW {errorNumber}, N'{code}', 1;\n");
            StringBuilder text = new();
            List<string> words = [];
            if (compiled.Recompile)
            {
                // The hint rides the one statement that reads the body, so the count lands in a variable first.
                const string CountWord = "count";
                string variable = ReservedSqlNames.Statement(ordinal, CountWord);
                words.Add(CountWord);
                _ = text.Append("DECLARE ").Append(variable).Append(" int;\n").Append(compiled.Prologue)
                    .Append("SELECT ").Append(variable).Append(" = COUNT(*) FROM (\n").Append(compiled.Body).Append(") AS [q]\nOPTION (RECOMPILE);\n")
                    .Append("IF ").Append(variable).Append(" <> ").Append(expectedName).Append(' ').Append(thrown);
            }
            else
            {
                _ = text.Append(compiled.Prologue)
                    .Append("IF (SELECT COUNT(*) FROM (\n").Append(compiled.Body).Append(") AS [q]) <> ").Append(expectedName).Append(' ').Append(thrown);
            }

            plan.Add(new PlannedStatement(ordinal, text.ToString(), Slots(compiled, arguments), words, [expected], Map.Metadata(countSpec.Root).ClrType));
            plan.Append(complete: null);
        }

        /// <inheritdoc />
        public BatchResult<EntityQueryResult<TEntity>> Query<TEntity>(EntityQuery<TEntity> query)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentException.ThrowIfNullOrWhiteSpace(query.Select, nameof(query));
            ArgumentNullException.ThrowIfNull(query.Restrictions, nameof(query));
            ArgumentNullException.ThrowIfNull(query.Children, nameof(query));
            EnsureComposing();
            EnsurePrologue();

            // --- An identical query already composed in this write epoch answers ---
            QueryStatementKey.Writer writer = QueryStatementKey.For("query").Add(typeof(TEntity).AssemblyQualifiedName).Add(query.Select)
                .Add(query.Filter).Add(query.OrderBy).Add(query.Skip).Add(query.Take).Add(query.Arguments).Add(query.Restrictions)
                .Add(query.AllProperties).Add(query.IncludeAncestors).Add(query.AncestorsFilter).Add(query.Children.Count);
            foreach (string child in query.Children)
            {
                _ = writer.Add(child);
            }

            QueryStatementKey key = writer.Build(Limits);
            if (_sharedQueries.TryGetValue(key, out object? shared))
            {
                return (BatchResult<EntityQueryResult<TEntity>>)shared;
            }

            // --- The root, its select completed with the keys the materializer needs, and its collections ---
            QueryServices services = _executor.Queries;
            EntityMetadata metadata = services.Metadata.Get(typeof(TEntity));
            QueryexSchemaMap map = Map;
            EntityDescriptor root = map.Descriptor(metadata);
            List<string> completion = Completion(root, query.Select);
            List<ChildNode> collections = Collections(metadata, query.Children);
            if (query.IncludeAncestors && root.TreeNode is null)
            {
                throw new ArgumentException($"The entity {root.Name} is no tree, so its query has no ancestors to include.", nameof(query));
            }

            QuerySpec spec = new()
            {
                Root = root,
                Select = completion.Count == 0 ? query.Select : $"{query.Select}, {string.Join(", ", completion)}",
                Filter = query.Filter,
                OrderBy = query.OrderBy,
                Skip = query.Skip,
                Take = query.Take,
                Restrictions = query.Restrictions,
                UndeclaredColumns = query.AllProperties ? [.. map.JsonColumns(metadata).Select(static property => property.Column)] : [],
            };

            // Roots fixed by ids need no capture: their collections restrict on the same ids. Ancestors
            // always climb from captured keys, so a root the filter hides never reveals its ancestors.
            KeySetRestriction? byIds = query.Restrictions.FirstOrDefault(restriction => string.Equals(restriction.Path, root.Key.Name, StringComparison.OrdinalIgnoreCase));
            bool capture = (collections.Count > 0 && byIds is null) || query.IncludeAncestors;

            // --- The plan: [keys twin], roots, [ancestors], then each collection, depth first ---
            QueryPlan plan = new(this);
            QueryState state = new();
            SqlIdentifier? keys = capture ? PlanKeys(plan, spec, query.Arguments, metadata.ClrType) : null;
            QuerySpec page = keys is SqlIdentifier captured ? Restricted(spec, captured) : spec;
            PlanRead(plan, page, query.Arguments, metadata.ClrType, rows => state.Roots = rows, compiled =>
            {
                if (ComputedItem(compiled, completion.Count, spec.UndeclaredColumns.Count) is QueryexColumn computed)
                {
                    throw new ArgumentException(
                        $"The entity query selects '{computed.Text}', which is computed. An entity query selects bare paths only; a computed value is a rows query's.",
                        nameof(query));
                }
            });
            if (query.IncludeAncestors)
            {
                PlanAncestors(plan, spec with { Filter = query.AncestorsFilter }, query.Arguments, keys!, metadata.ClrType, rows => state.Ancestors = rows);
            }

            string parentSource = keys?.Name ?? byIds?.TableSource ?? string.Empty;
            PlanCollections(plan, collections, parentSource, query.AllProperties, state);

            // --- After the last statement: materialise the roots, stitch the collections, gather the related ---
            BatchResult<EntityQueryResult<TEntity>> result = new();
            EntityMaterializer materializer = services.Materializer;
            plan.Append(cursor =>
            {
                RelatedCollector related = new();
                Materialized roots = materializer.Read(map, metadata, state.Roots!, related);
                Stitch(materializer, map, metadata, roots.Instances, collections, state, related);
                if (state.Ancestors is QueryRowSet ancestorRows)
                {
                    Materialized ancestors = materializer.Read(map, metadata, ancestorRows, related);
                    EntityMaterializer.AddWhole(related, metadata, ancestors.Instances, ancestors.Carried);
                }

                cursor.Complete(result, new EntityQueryResult<TEntity>([.. roots.Instances.Cast<TEntity>()], materializer.Build(related)));
            });

            _sharedQueries[key] = result;
            if (keys is SqlIdentifier remembered)
            {
                _capturedKeys[result] = remembered;
            }

            return result;
        }

        /// <summary>
        ///     The key-capture table a rows or entity query statement filled, for later statements to
        ///     restrict on; null when the statement captured none.
        /// </summary>
        /// <param name="handle">The handle <see cref="Rows" /> or <see cref="Query{TEntity}" /> returned.</param>
        /// <returns>The table, or null.</returns>
        internal SqlIdentifier? KeysOf(object handle)
        {
            ArgumentNullException.ThrowIfNull(handle);
            return _capturedKeys.GetValueOrDefault(handle);
        }

        // ===================== Query helpers =====================

        /// <summary>The row limit of a capped count: the cap plus one, so a count past the cap shows as cap + 1.</summary>
        /// <param name="cap">The cap.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>The limit.</returns>
        private static int CountTake(int cap, string parameterName)
        {
            return cap is >= 0 and < int.MaxValue
                ? cap + 1
                : throw new ArgumentOutOfRangeException(parameterName, cap, "A count cap is zero or more, and less than the largest integer.");
        }

        /// <summary>Whether a spec pages, which lets its ordering stand inside a derived table.</summary>
        /// <param name="spec">The spec.</param>
        /// <returns>True when it skips or takes.</returns>
        private static bool Paged(QuerySpec spec)
        {
            return spec.Skip is not null || spec.Take is not null;
        }

        /// <summary>A spec narrowed to the keys a twin captured: its filter, restrictions and paging are the twin's already.</summary>
        /// <param name="spec">The spec.</param>
        /// <param name="keys">The captured keys.</param>
        /// <returns>The narrowed spec, its ordering kept with the twin's tie order.</returns>
        private static QuerySpec Restricted(QuerySpec spec, SqlIdentifier keys)
        {
            return spec with
            {
                Filter = null,
                Restrictions = [new KeySetRestriction(spec.Root.Key.Name, keys.Name)],
                OrderBy = TieBroken(spec),
                Skip = null,
                Take = null,
            };
        }

        /// <summary>
        ///     A spec's ordering with the root key as its last item, so a statement that drops the
        ///     spec's paging still orders rows that tie as the paged spec did.
        /// </summary>
        /// <param name="spec">The spec.</param>
        /// <returns>
        ///     The ordering, the key appended unless it is already the last item; null for a spec with
        ///     none, which the language refuses to page, so it has no paged order to keep.
        /// </returns>
        private static string? TieBroken(QuerySpec spec)
        {
            string key = spec.Root.Key.Name;
            if (string.IsNullOrWhiteSpace(spec.OrderBy))
            {
                return null;
            }

            // The last item, read off after the last comma: an item that is not the bare key, with or
            // without a direction, gets the key after it.
            string[] last = spec.OrderBy[(spec.OrderBy.LastIndexOf(',') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool endsInKey = last.Length is 1 or 2
                && string.Equals(last[0], key, StringComparison.OrdinalIgnoreCase)
                && (last.Length == 1 || last[1].Equals("asc", StringComparison.OrdinalIgnoreCase) || last[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
            return endsInKey ? spec.OrderBy : $"{spec.OrderBy}, {key}";
        }

        /// <summary>The <c>OPTION</c> clause a statement that embeds a compiled body carries.</summary>
        /// <param name="compiled">The compiled query.</param>
        /// <returns>The clause and its line feed, or nothing.</returns>
        private static string Option(CompiledQuery compiled)
        {
            return compiled.Recompile ? "OPTION (RECOMPILE)\n" : string.Empty;
        }

        /// <summary>The type a key-capture table's key column is declared with.</summary>
        /// <param name="root">The root entity.</param>
        /// <returns>The type's keyword.</returns>
        private static string KeyType(EntityDescriptor root)
        {
            return root.Key.StoreType is QueryexStoreType store && KeyTypes.TryGetValue(store.Family, out string? keyword)
                ? keyword
                : throw new InvalidOperationException($"The key of the entity {root.Name} is neither int nor bigint, so its keys cannot be captured.");
        }

        /// <summary>The refusal of a compilation that reported diagnostics: a composer bug.</summary>
        /// <param name="subject">What was compiled, for the message.</param>
        /// <param name="diagnostics">The diagnostics.</param>
        /// <param name="parameterName">The caller's parameter name.</param>
        /// <returns>The exception.</returns>
        private static ArgumentException Refused(string subject, IReadOnlyList<QueryexDiagnostic> diagnostics, string parameterName)
        {
            string described = string.Join("; ", diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code} at {diagnostic.Location ?? "the text"} [{diagnostic.Span.Start}..{diagnostic.Span.End})"
                + (diagnostic.Arguments.Count == 0 ? string.Empty : $" ({string.Join(", ", diagnostic.Arguments.Select(static argument => $"{argument.Key}={argument.Value}"))})")));
            return new ArgumentException($"{subject} did not compile: {described}.", parameterName);
        }

        /// <summary>The reader of a statement whose result set is a compiled query's rows.</summary>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <param name="columns">The compiled columns.</param>
        /// <param name="store">Takes the rows.</param>
        /// <returns>The reader.</returns>
        private static StatementReader Read(int ordinal, IReadOnlyList<QueryexColumn> columns, Action<QueryRowSet> store)
        {
            return async (cursor, cancellationToken) =>
            {
                DbDataReader reader = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                store(await QueryexResultReader.ReadAsync(reader, columns, cursor, cancellationToken).ConfigureAwait(false));
            };
        }

        /// <summary>The first computed select item of the caller's, which an entity query refuses: it selects bare paths.</summary>
        /// <param name="compiled">The compiled root query.</param>
        /// <param name="completion">How many items the batch appended after the caller's.</param>
        /// <param name="undeclared">How many undeclared columns follow them.</param>
        /// <returns>The item's column, or null when every item is a bare path.</returns>
        private static QueryexColumn? ComputedItem(CompiledQuery compiled, int completion, int undeclared)
        {
            return compiled.Columns.Take(compiled.Columns.Count - completion - undeclared).FirstOrDefault(static column => column.Path is null);
        }

        /// <summary>
        ///     The collections an entity query names, as a tree: a dotted name adds its grandchild
        ///     collection under its child collection, which is then loaded too.
        /// </summary>
        /// <param name="root">The root entity.</param>
        /// <param name="names">The collection names.</param>
        /// <returns>The root's collections to load.</returns>
        /// <exception cref="ArgumentException">A name names no collection.</exception>
        private static List<ChildNode> Collections(EntityMetadata root, IReadOnlyList<string> names)
        {
            List<ChildNode> top = [];
            foreach (string name in names)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(names));
                EntityMetadata owner = root;
                List<ChildNode> level = top;
                foreach (string segment in name.Split('.'))
                {
                    ChildCollectionMetadata collection = owner.Children.FirstOrDefault(candidate => string.Equals(candidate.Property, segment, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException($"The entity {owner.Name} has no child collection '{segment}' (in '{name}').", nameof(names));
                    ChildNode? node = level.FirstOrDefault(candidate => ReferenceEquals(candidate.Collection, collection));
                    if (node is null)
                    {
                        node = new ChildNode(collection);
                        level.Add(node);
                    }

                    level = node.Children;
                    owner = collection.Child;
                }
            }

            return top;
        }

        /// <summary>Stitches each loaded collection onto its parents, depth first.</summary>
        /// <param name="materializer">The materializer.</param>
        /// <param name="map">The schema map.</param>
        /// <param name="parent">The parents' entity.</param>
        /// <param name="parents">The parents.</param>
        /// <param name="collections">The parents' collections.</param>
        /// <param name="state">The rows read.</param>
        /// <param name="related">The query's related instances.</param>
        private static void Stitch(
            EntityMaterializer materializer, QueryexSchemaMap map, EntityMetadata parent, IReadOnlyList<object> parents,
            IReadOnlyList<ChildNode> collections, QueryState state, RelatedCollector related)
        {
            foreach (ChildNode node in collections)
            {
                Materialized children = materializer.Read(map, node.Collection.Child, state.Children[node], related);
                materializer.Stitch(parent, node.Collection, parents, children.Instances);
                Stitch(materializer, map, node.Collection.Child, children.Instances, node.Children, state, related);
            }
        }

        /// <summary>
        ///     The items an entity query's select needs beyond the caller's: the root key, and for
        ///     every navigation prefix a path crosses, the foreign key on its owner and the target's
        ///     key, so each related instance is keyed and its owner can be matched to it.
        /// </summary>
        /// <param name="root">The root entity.</param>
        /// <param name="select">The caller's select.</param>
        /// <returns>The items to append, none already selected.</returns>
        /// <exception cref="ArgumentException">The select does not parse or resolve.</exception>
        private List<string> Completion(EntityDescriptor root, string select)
        {
            // The engine caches the discovery, so a repeated select costs a lookup.
            DiscoveryResult discovery = _executor.Queries.Engine.DiscoverQuery(
                new QueryDiscoverySpec { Select = select },
                new DiscoveryOptions { Schema = Map.Schema, Root = root, Limits = Limits });
            if (discovery.Diagnostics.Count > 0)
            {
                throw Refused($"The select of the {root.Name} query", discovery.Diagnostics, "query");
            }

            HashSet<string> selected = new(discovery.Paths.Select(static path => string.Join('.', path.Segments)), StringComparer.OrdinalIgnoreCase);
            List<string> completion = [];
            void Need(string path)
            {
                if (selected.Add(path))
                {
                    completion.Add(path);
                }
            }

            Need(root.Key.Name);
            foreach (PathUse path in discovery.Paths)
            {
                EntityDescriptor owner = root;
                int walked = 0;
                for (int depth = 0; depth < path.Segments.Count - 1; depth++)
                {
                    NavigationDescriptor? navigation = owner.Navigations.FirstOrDefault(candidate => string.Equals(candidate.Name, path.Segments[depth], StringComparison.OrdinalIgnoreCase));
                    if (navigation is null)
                    {
                        // Not a navigation step: the compile reports the path.
                        break;
                    }

                    string ownerPrefix = depth == 0 ? string.Empty : string.Join('.', path.Segments.Take(depth)) + ".";
                    Need(ownerPrefix + navigation.ForeignKey.Name);
                    Need(string.Join('.', path.Segments.Take(depth + 1)) + "." + navigation.Target.Key.Name);
                    owner = navigation.Target;
                    walked++;
                }

                // A column the schema declares but the entity's class does not hold, such as a
                // tree's node, would compile and run, then fail in the materializer.
                if (walked == path.Segments.Count - 1)
                {
                    string property = path.Segments[^1];
                    EntityMetadata metadata = Map.Metadata(owner);
                    if (!metadata.Properties.Any(candidate => string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new ArgumentException(
                            $"The entity query selects '{string.Join('.', path.Segments)}', which the class of {metadata.Name} does not hold. An entity query selects the properties of its classes; a rows query reads any column.",
                            nameof(select));
                    }
                }
            }

            return completion;
        }

        /// <summary>Compiles a spec at an ordinal, raising diagnostics as a compose-time refusal.</summary>
        /// <param name="spec">The spec.</param>
        /// <param name="arguments">The declared parameters, or null.</param>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <returns>The compiled query.</returns>
        private CompiledQuery Compile(QuerySpec spec, QueryArguments? arguments, int ordinal)
        {
            QueryCompilationOptions options = new()
            {
                Schema = Map.Schema,
                QueryexVersion = QueryexLanguage.Version,
                Parameters = arguments?.Declarations ?? [],
                OmittedParameters = arguments?.Omitted ?? [],
                HasUser = Context.UserId is not null,
                BatchOrdinal = ordinal,
                Limits = Limits,
            };
            QueryexResult<CompiledQuery> compiled = _executor.Queries.Engine.CompileQuery(spec, options);
            return compiled.Value ?? throw Refused($"The {spec.Root.Name} query", compiled.Diagnostics, nameof(spec));
        }

        /// <summary>The bound parameters of a compiled query's slots.</summary>
        /// <param name="compiled">The compiled query.</param>
        /// <param name="arguments">The declared parameters, or null.</param>
        /// <returns>The parameters.</returns>
        private IReadOnlyList<ScalarParameter> Slots(CompiledQuery compiled, QueryArguments? arguments)
        {
            return QueryexSlots.Parameters(compiled, Context, arguments?.Values ?? NoValues);
        }

        /// <summary>
        ///     Plans the keys-only twin of a spec: its keys, under its filter, restrictions, ordering
        ///     and paging, inserted into a table later statements restrict on.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="spec">The spec.</param>
        /// <param name="arguments">The declared parameters, or null.</param>
        /// <param name="entityType">The root's class, for error mapping.</param>
        /// <returns>The table.</returns>
        private SqlIdentifier PlanKeys(QueryPlan plan, QuerySpec spec, QueryArguments? arguments, Type entityType)
        {
            int ordinal = plan.NextOrdinal;
            string key = spec.Root.Key.Name;

            // An ordering without paging decides nothing about which keys are captured.
            QuerySpec twin = spec with { Select = key, OrderBy = Paged(spec) ? spec.OrderBy : null, UndeclaredColumns = [] };
            CompiledQuery compiled = Compile(twin, arguments, ordinal);

            const string Word = ReservedSqlNames.StatementNames.Keys;
            string table = ReservedSqlNames.Statement(ordinal, Word);
            string text = $"DECLARE {table} TABLE ([Id] {KeyType(spec.Root)} PRIMARY KEY);\n{compiled.Prologue}INSERT INTO {table} ([Id])\n{compiled.Body}{Option(compiled)};\n";
            plan.Add(new PlannedStatement(ordinal, text, Slots(compiled, arguments), [Word], EntityType: entityType));
            return new SqlIdentifier(table);
        }

        /// <summary>Plans a statement that reads a spec's rows.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="spec">The spec.</param>
        /// <param name="arguments">The declared parameters, or null.</param>
        /// <param name="entityType">The root's class, for error mapping.</param>
        /// <param name="store">Takes the rows.</param>
        /// <param name="inspect">Inspects the compiled query before it is planned, or null.</param>
        private void PlanRead(QueryPlan plan, QuerySpec spec, QueryArguments? arguments, Type entityType, Action<QueryRowSet> store, Action<CompiledQuery>? inspect = null)
        {
            int ordinal = plan.NextOrdinal;
            CompiledQuery compiled = Compile(spec, arguments, ordinal);
            inspect?.Invoke(compiled);
            plan.Add(new PlannedStatement(ordinal, compiled.Sql, Slots(compiled, arguments), EntityType: entityType, Reader: Read(ordinal, compiled.Columns, store)));
        }

        /// <summary>
        ///     Plans a capped count of a spec's rows: its keys, ordered and limited to the cap plus
        ///     one, counted; for a grouped spec, its groups, capped the same way.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="spec">The spec.</param>
        /// <param name="arguments">The declared parameters, or null.</param>
        /// <param name="take">The cap plus one.</param>
        /// <param name="entityType">The root's class, for error mapping.</param>
        /// <param name="store">Takes the count.</param>
        private void PlanCount(QueryPlan plan, QuerySpec spec, QueryArguments? arguments, int take, Type entityType, Action<int> store)
        {
            int ordinal = plan.NextOrdinal;
            string key = spec.Root.Key.Name;

            // A group has no key of its own: the grouped body is counted as it stands, its skip
            // dropped. Only an ordering can be limited, so an unordered one is counted whole and the
            // count is capped as it is read; its groups are all computed either way.
            QuerySpec counted = !spec.Aggregate
                ? spec with { Select = key, OrderBy = key, Skip = null, Take = take, UndeclaredColumns = [] }
                : string.IsNullOrWhiteSpace(spec.OrderBy)
                    ? spec with { Skip = null, Take = null, UndeclaredColumns = [] }
                    : spec with { Skip = null, Take = take, UndeclaredColumns = [] };
            CompiledQuery compiled = Compile(counted, arguments, ordinal);

            string text = $"{compiled.Prologue}SELECT COUNT(*) AS [Count] FROM (\n{compiled.Body}) AS [q]\n{Option(compiled)};\n";
            plan.Add(new PlannedStatement(ordinal, text, Slots(compiled, arguments), EntityType: entityType, Reader: async (cursor, cancellationToken) =>
            {
                // COUNT(*) without grouping answers one row.
                DbDataReader reader = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Batch statement {ordinal} counted no row."));
                }

                cursor.CountRows(1);
                store(Math.Min(reader.GetInt32(0), take));
            }));
        }

        /// <summary>
        ///     Plans a tree page's ancestors: the nodes above the captured keys that are not among
        ///     them, then the spec's rows restricted to those nodes, ordering kept, no paging.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="spec">The spec, its filter the ancestors' own.</param>
        /// <param name="arguments">The declared parameters, or null.</param>
        /// <param name="keys">The page's captured keys.</param>
        /// <param name="entityType">The root's class, for error mapping.</param>
        /// <param name="store">Takes the rows.</param>
        private void PlanAncestors(QueryPlan plan, QuerySpec spec, QueryArguments? arguments, SqlIdentifier keys, Type entityType, Action<QueryRowSet> store)
        {
            EntityDescriptor root = spec.Root;
            PropertyDescriptor node = root.TreeNode
                ?? throw new ArgumentException($"The entity {root.Name} is no tree, so its page has no ancestors to include.", nameof(spec));
            int ordinal = plan.NextOrdinal;

            const string Word = ReservedSqlNames.StatementNames.Ancestors;
            string table = ReservedSqlNames.Statement(ordinal, Word);
            CompiledQuery compiled = Compile(
                spec with { Restrictions = [new KeySetRestriction(root.Key.Name, table)], OrderBy = TieBroken(spec), Skip = null, Take = null }, arguments, ordinal);

            string id = SqlNames.QuoteObjectName(root.Key.Column);
            string path = SqlNames.QuoteObjectName(node.Column);
            string text =
                $"DECLARE {table} TABLE ([Id] {KeyType(root)} PRIMARY KEY);\n" +
                $"INSERT INTO {table} ([Id])\n" +
                $"SELECT [a].{id} FROM {root.Source} AS [a]\n" +
                $"WHERE [a].{id} NOT IN (SELECT [Id] FROM {keys.Name})\n" +
                $"  AND EXISTS (SELECT 1 FROM {root.Source} AS [p] JOIN {keys.Name} AS [k] ON [k].[Id] = [p].{id}\n" +
                $"              WHERE [p].{path}.IsDescendantOf([a].{path}) = 1 AND [p].{id} <> [a].{id});\n" +
                compiled.Sql;
            plan.Add(new PlannedStatement(ordinal, text, Slots(compiled, arguments), [Word], EntityType: entityType, Reader: Read(ordinal, compiled.Columns, store)));
        }

        /// <summary>
        ///     Plans the child queries of a level of collections: each child's declared properties,
        ///     ordered by its key, restricted to the parents' keys; a child with collections of its
        ///     own captures its keys for them.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="collections">The level's collections.</param>
        /// <param name="parentSource">The table holding the parents' keys.</param>
        /// <param name="allProperties">Whether the JSON columns are read too.</param>
        /// <param name="state">Where the rows land.</param>
        private void PlanCollections(QueryPlan plan, IReadOnlyList<ChildNode> collections, string parentSource, bool allProperties, QueryState state)
        {
            foreach (ChildNode node in collections)
            {
                EntityMetadata child = node.Collection.Child;
                EntityDescriptor descriptor = Map.Descriptor(child);
                QuerySpec spec = new()
                {
                    Root = descriptor,
                    Select = string.Join(", ", Map.Declared(child).Select(static property => property.Name)),
                    OrderBy = descriptor.Key.Name,
                    Restrictions = [new KeySetRestriction(node.Collection.ParentKey.Name, parentSource)],
                    UndeclaredColumns = allProperties ? [.. Map.JsonColumns(child).Select(static property => property.Column)] : [],
                };

                SqlIdentifier? keys = node.Children.Count > 0 ? PlanKeys(plan, spec, arguments: null, child.ClrType) : null;
                PlanRead(plan, keys is SqlIdentifier captured ? Restricted(spec, captured) : spec, arguments: null, child.ClrType, rows => state.Children[node] = rows);
                if (keys is SqlIdentifier own)
                {
                    PlanCollections(plan, node.Children, own.Name, allProperties, state);
                }
            }
        }

        /// <summary>
        ///     One statement of a query plan, compiled at the ordinal it will take: its text, its
        ///     bound slots, the statement-local names it declares, its scalar parameters and its reader.
        /// </summary>
        /// <param name="Ordinal">The ordinal the statement takes.</param>
        /// <param name="Text">The text.</param>
        /// <param name="Parameters">The bound engine slots.</param>
        /// <param name="Words">The words of the statement-local tables and variables it declares.</param>
        /// <param name="Scalars">The values of its <c>@tb{b}_p{n}</c> parameters, in order.</param>
        /// <param name="EntityType">The class its rows are of, for error mapping.</param>
        /// <param name="Reader">Reads its result set, or null when it returns none.</param>
        private sealed record PlannedStatement(
            int Ordinal,
            string Text,
            IReadOnlyList<ScalarParameter> Parameters,
            IReadOnlyList<string>? Words = null,
            IReadOnlyList<object?>? Scalars = null,
            Type? EntityType = null,
            StatementReader? Reader = null);

        /// <summary>
        ///     The statements one query call appends, compiled at the ordinals they will take before
        ///     any is appended, so a refused compile or an exhausted parameter budget leaves the batch
        ///     as it was.
        /// </summary>
        /// <param name="batch">The batch.</param>
        private sealed class QueryPlan(DataBatch batch)
        {
            /// <summary>The statements, in ordinal order.</summary>
            private readonly List<PlannedStatement> _planned = [];

            /// <summary>The ordinal the next planned statement takes.</summary>
            public int NextOrdinal => batch._statements.Count + _planned.Count;

            /// <summary>Plans the next statement.</summary>
            /// <param name="statement">The statement, compiled at <see cref="NextOrdinal" />.</param>
            public void Add(PlannedStatement statement)
            {
                if (statement.Ordinal != NextOrdinal)
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture, $"A query statement planned at ordinal {statement.Ordinal} would take ordinal {NextOrdinal}."));
                }

                _planned.Add(statement);
            }

            /// <summary>
            ///     Appends every planned statement; the last one that reads a result set then completes
            ///     the call's result.
            /// </summary>
            /// <param name="complete">Completes the call's result from what the readers stored, or null for none.</param>
            /// <exception cref="InvalidOperationException">The statements would pass the batch's parameter budget.</exception>
            public void Append(Action<ResultSetCursor>? complete)
            {
                // --- The parameter budget, for the whole call, before anything is appended ---
                HashSet<string> added = new(StringComparer.OrdinalIgnoreCase);
                foreach (PlannedStatement statement in _planned)
                {
                    added.UnionWith(statement.Parameters.Select(static parameter => parameter.Name));
                    for (int index = 0; index < (statement.Scalars?.Count ?? 0); index++)
                    {
                        _ = added.Add(ReservedSqlNames.Statement(statement.Ordinal, ReservedSqlNames.StatementNames.Scalar(index)));
                    }
                }

                added.ExceptWith(batch._parameterNames);
                if (batch._parameterNames.Count + added.Count > BatchAssembly.MaxParameters)
                {
                    throw BatchAssembly.TooManyParameters(batch._parameterNames.Count + added.Count);
                }

                // --- The statements, the completion riding the last reader ---
                int last = _planned.FindLastIndex(static statement => statement.Reader is not null);
                if (complete is not null && last < 0)
                {
                    throw new InvalidOperationException("A query call that completes a result plans a statement that reads one.");
                }

                for (int index = 0; index < _planned.Count; index++)
                {
                    PlannedStatement statement = _planned[index];
                    StatementReader? reader = statement.Reader;
                    if (index == last && complete is not null)
                    {
                        StatementReader read = reader!;
                        reader = async (cursor, cancellationToken) =>
                        {
                            await read(cursor, cancellationToken).ConfigureAwait(false);
                            complete(cursor);
                        };
                    }

                    StatementDraft draft = batch.Begin();
                    try
                    {
                        if (draft.Ordinal != statement.Ordinal)
                        {
                            throw new InvalidOperationException(string.Create(
                                CultureInfo.InvariantCulture, $"A query statement compiled for ordinal {statement.Ordinal} was begun at {draft.Ordinal}."));
                        }

                        draft.EntityType = statement.EntityType;
                        foreach (string word in statement.Words ?? [])
                        {
                            _ = draft.Name(word);
                        }

                        foreach (object? scalar in statement.Scalars ?? [])
                        {
                            _ = draft.Scalar(scalar);
                        }

                        foreach (ScalarParameter parameter in statement.Parameters)
                        {
                            draft.Engine(parameter);
                        }

                        batch.Append(draft, statement.Text, reader: reader);
                    }
                    catch
                    {
                        batch.Discard(draft);
                        throw;
                    }
                }
            }
        }

        /// <summary>What a rows call's readers store, for the last one to complete the page with.</summary>
        private sealed class RowsState
        {
            /// <summary>The page's rows.</summary>
            public QueryRowSet? Rows { get; set; }

            /// <summary>The capped count, when asked for.</summary>
            public int? Count { get; set; }

            /// <summary>The ancestors' rows, when asked for.</summary>
            public QueryRowSet? Ancestors { get; set; }
        }

        /// <summary>What an entity query's readers store, for the last one to materialise.</summary>
        private sealed class QueryState
        {
            /// <summary>The roots' rows.</summary>
            public QueryRowSet? Roots { get; set; }

            /// <summary>The ancestors' rows, when asked for.</summary>
            public QueryRowSet? Ancestors { get; set; }

            /// <summary>Each collection's rows.</summary>
            public Dictionary<ChildNode, QueryRowSet> Children { get; } = [];
        }

        /// <summary>One collection an entity query loads, with the collections loaded under it.</summary>
        /// <param name="collection">The collection.</param>
        private sealed class ChildNode(ChildCollectionMetadata collection)
        {
            /// <summary>The collection.</summary>
            public ChildCollectionMetadata Collection { get; } = collection;

            /// <summary>Its children's collections to load.</summary>
            public List<ChildNode> Children { get; } = [];
        }
    }
}
