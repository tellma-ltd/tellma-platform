// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Queryex;
using Xunit;

namespace Tellma.Core.Testing.Crud
{
    // Spec 0015 §16.2: the round-trip budget of every operation, measured by the platform's own
    // count and, when the harness observes the driver, by the server's.
    /// <content>The round-trip budget.</content>
    public abstract partial class StackConformanceTests<TEntity, TKey>
    {
        /// <summary>
        ///     Each read takes its round-trip budget, measured over two rows so a statement per row
        ///     shows: one for a query (with its count, and with its ancestors on a tree), a details
        ///     read, a read by parent ids, a child surface's query, an Excel page with every
        ///     collection at every depth, an Excel lookup, an exportable child surface's Excel page, a
        ///     display and an editable export of the two rows, and an import's inspection of the
        ///     caller's upload; none for a cached list, or two on a table beyond the list's row limit.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Each_read_takes_its_round_trip_budget()
        {
            StackConformanceCaller writer = await WriterAsync();
            TEntity first = await CreateNewAsync();
            TEntity second = await CreateNewAsync();
            AssertTwoRowsAtEveryDepth(await LoadAsync(first.Id));
            TKey[] ids = [first.Id, second.Id];

            // --- Each read the stack serves and its budget ---
            List<(string What, int Budget, Func<IServiceProvider, Task> Call)> reads = [];
            if (Serves(StackOperations.Query))
            {
                QueryRequest counted = ByIds(ids);
                counted.IncludeCount = true;
                reads.Add(("a query", 1, scope => Service(scope).QueryAsync(ByIds(ids), Token)));
                reads.Add(("a query with its count", 1, scope => Service(scope).QueryAsync(counted, Token)));
            }

            if (Serves(StackOperations.Query, Tree))
            {
                // --- Two parents, each with a child, so the read by parent ids answers a row of each ---
                _ = await CreateAsync(NewChildOf(first, NextOrdinal()));
                _ = await CreateAsync(NewChildOf(second, NextOrdinal()));
                QueryRequest ancestors = ByIds(ids);
                ancestors.IncludeAncestors = true;
                reads.Add(("a query with its ancestors", 1, scope => Service(scope).QueryAsync(ancestors, Token)));
                reads.Add(("a read by parent ids", 1, scope => Service(scope).Pipeline.GetByParentIdsAsync(ByParents(ids), Token)));
            }

            if (Serves(StackOperations.Details))
            {
                reads.Add(("a details read by id", 1, scope => Service(scope).GetByIdAsync(first.Id, DetailsRequest.None, Token)));
                reads.Add(("a details read by ids", 1, scope => Service(scope).GetByIdsAsync(ids, DetailsRequest.None, Token)));
            }

            foreach (ChildQueryDescriptor child in ChildSurfaces())
            {
                QueryRequest request = new() { Select = "Id", Take = 2 };
                reads.Add(($"a query of {child.Resource}", 1, scope => scope.GetRequiredService<IChildQueryService>().QueryAsync(child.Resource, request, Token)));
            }

            // --- The Excel rows (spec 0015 §16.2): one round trip per page, every collection at every
            // depth riding it while each holds fewer rows than the take ceiling; a lookup's flat
            // reads in one; and each exportable child surface's page by query ---
            if (Serves(StackOperations.Export))
            {
                ExcelQuery lookup = new(Metadata.Name, [Metadata.Key.Name], null, new KeySetRestriction(Metadata.Key.Name, "@lookup"), [.. ids.Select(static id => (object)long.CreateChecked(id))], null, null, null);
                reads.Add(("an Excel page with its collections", 1, scope => ExcelRowsAsync(Service(scope).ExcelRowSource(ExportByIds(ids)), ExcelTree())));
                reads.Add(("an Excel lookup", 1, scope => Service(scope).ExcelRowSource(new ExportSource.All()).ReadAsync([lookup], Token)));
                foreach (ChildQueryDescriptor child in ChildSurfaces().Where(static child => child.Exportable))
                {
                    ExportSource.ByQuery source = new(null, null, null, null, null, IncludeInactive: false);
                    ExcelQuery page = new(child.Resource, ["Id"], null, null, null, Take: 2, CountCap: 2, null);
                    reads.Add(($"an Excel page of {child.Resource}", 1, scope => ExcelRowsAsync(scope.GetRequiredService<IChildQueryService>().ExcelRowSource(child.Resource, source), page)));
                }
            }

            // --- The two exports (spec 0019 §1.4): one round trip per page of the take ceiling, so
            // one for the two rows, the editable shape's collections riding it ---
            ApiActionDescriptor[] workbookActions = [.. Stack.Actions.OfType<ApiActionDescriptor>()];
            long[] exported = [.. ids.Select(static id => long.CreateChecked(id))];
            if (workbookActions.FirstOrDefault(static action => action.Name == "export") is ApiActionDescriptor displayExport)
            {
                ExportRequest request = new() { Ids = exported, Columns = [new(Metadata.Key.Name, null)] };
                reads.Add(("a display export of the two rows", 1, scope => ExportAsync(scope, displayExport, request)));
            }

            if (workbookActions.FirstOrDefault(static action => action.Name == "export-for-import") is ApiActionDescriptor editableExport)
            {
                ExportForImportRequest request = new() { Ids = exported };
                reads.Add(("an editable export of the two rows", 1, scope => ExportAsync(scope, editableExport, request)));
            }

            // --- An import's inspection (spec 0019 §7.1): the caller's staged upload, an editable
            // export of the two rows, resolved in one round trip and read locally after it ---
            if (workbookActions.FirstOrDefault(static action => action.RequestType == typeof(InspectImportRequest)) is ApiActionDescriptor inspect
                && workbookActions.FirstOrDefault(static action => action.RequestType == typeof(ExportForImportRequest)) is ApiActionDescriptor editable)
            {
                int fileId = (await CountedAsync(writer, scope => StageEditableExportAsync(scope, editable, ids))).Result;
                InspectImportRequest request = new() { FileId = fileId };
                reads.Add(("an import inspection of an editable export", 1, scope => scope.GetRequiredService<IApiActionInvoker>().InvokeAsync(inspect, request, Token)));
            }

            // --- Each read once to warm its caller, its plan and its caches, then counted ---
            foreach ((string what, int budget, Func<IServiceProvider, Task> call) in reads)
            {
                _ = await CountedAsync(writer, async scope =>
                {
                    await call(scope);
                    return 0;
                });
                AssertTrips(budget, await CountedAsync(writer, async scope =>
                {
                    await call(scope);
                    return 0;
                }), what);
            }

            // --- The cached list: one read fills the cache, and the next costs nothing; beyond the
            // list's row limit, every read probes and re-reads ---
            if (Serves(StackOperations.Details, Cacheable))
            {
                bool? oversized = await OversizedAsync();
                Assert.SkipWhen(oversized is null, $"The stack {Stack.Resource} serves no query to count its rows by, so the suite cannot tell whether its cached list is beyond its row limit.");
                _ = await CountedAsync(writer, scope => Service(scope).GetAllCachedAsync(DetailsRequest.None, Token));
                AssertTrips(oversized == true ? 2 : 0, await CountedAsync(writer, scope => Service(scope).GetAllCachedAsync(DetailsRequest.None, Token)), "a cached list read");
            }
        }

        /// <summary>
        ///     Each write takes its round-trip budget, measured over two rows so a statement per row
        ///     or per id shows: a create one, or two when it has anything to load; an update two; an
        ///     import updating the two by id the upload's read, one read of every lookup together
        ///     when a reference is looked up, the hydration and the update's two; a delete by query
        ///     one; a delete by ids or with descendants and a built-in action one, or two when a
        ///     validator judges them; a custom action two; each plus the validators' dependent rounds
        ///     where they apply.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Each_write_takes_its_round_trip_budget()
        {
            Assert.SkipUnless(Serves(StackOperations.Save) || Serves(StackOperations.Delete), $"The stack {Stack.Resource} serves no write.");
            StackConformanceCaller writer = await WriterAsync();

            // --- A first create warms the caller and the id buffers; its two rows are the targets below ---
            TEntity first = await CreateNewAsync();
            TEntity second = await CreateNewAsync();
            TKey[] ids = [first.Id, second.Id];

            if (Serves(StackOperations.Save))
            {
                // --- A create of two: their persist and read-back, behind a validation round when they load ---
                TEntity[] created = [NewValidEntity(NextOrdinal()), NewValidEntity(NextOrdinal())];
                AssertTwoRowsAtEveryDepth(created[0]);
                AssertTrips(CreateBudget(created), await CountedAsync(writer, scope => Service(scope).SaveAsync(created, new SaveOptions(), Token)), "a create of two");

                // --- An update of two: the before images on the first round trip, the persist on the second ---
                TEntity[] loaded = [await LoadAsync(first.Id), await LoadAsync(second.Id)];
                Change(loaded[0], NextOrdinal());
                Change(loaded[1], NextOrdinal());
                AssertTrips(2 + DependentValidationRounds, await CountedAsync(writer, scope => Service(scope).SaveAsync(loaded, new SaveOptions(), Token)), "an update of two");
            }

            // --- An import updating the two rows by id (spec 0019 §1.4): the upload's read, the
            // lookups in one read when a reference is looked up, the hydration, then the update's
            // two. A first import warms the caller, the plans and the header caches; a fresh export
            // after it carries the stamps that import left, so the counted one conflicts with none ---
            ApiActionDescriptor[] workbookActions = [.. Stack.Actions.OfType<ApiActionDescriptor>()];
            if (workbookActions.FirstOrDefault(static action => action.RequestType == typeof(ImportRequest)) is ApiActionDescriptor import
                && workbookActions.FirstOrDefault(static action => action.RequestType == typeof(ExportForImportRequest)) is ApiActionDescriptor editable)
            {
                int warm = (await CountedAsync(writer, scope => StageEditableExportAsync(scope, editable, ids))).Result;
                _ = await CountedAsync(writer, scope => UpdateByImportAsync(scope, import, warm));
                int fileId = (await CountedAsync(writer, scope => StageEditableExportAsync(scope, editable, ids))).Result;
                HashSet<long> imported = [.. ids.Select(static id => long.CreateChecked(id))];
                bool looksUp = false;
                foreach (TKey id in ids)
                {
                    looksUp |= LooksUp(Metadata, await LoadAsync(id), parentKey: null, imported);
                }

                AssertTrips(
                    1 + (looksUp ? 1 : 0) + 1 + 2 + DependentValidationRounds,
                    await CountedAsync(writer, scope => UpdateByImportAsync(scope, import, fileId)),
                    "an import updating two by id");
            }

            // --- The built-in actions: the update with its pre-check, behind a validation round when judged ---
            if (ServesActivation())
            {
                int budget = ValidatesActions ? 2 : 1;
                AssertTrips(budget, await CountedAsync(writer, scope => Service(scope).ExecuteActionAsync(DeactivateAction, ids, null, new ActionOptions(), Token)), "a deactivate of two");
                AssertTrips(budget, await CountedAsync(writer, scope => Service(scope).ExecuteActionAsync(ActivateAction, ids, null, new ActionOptions(), Token)), "an activate of two");
            }

            // --- Each custom action: its targets with the first round, its persist on the second,
            // and the rounds its loads depend on ---
            foreach (EntityActionDescriptor action in Stack.Actions.OfType<EntityActionDescriptor>().Where(static action => !action.IsBuiltIn))
            {
                TKey[] targets = action.SingleTarget ? [first.Id] : ids;
                AssertTrips(
                    2 + DependentActionRounds(action),
                    await CountedAsync(writer, scope => Service(scope).ExecuteActionAsync(action.Name, targets, ActionArguments(action), new ActionOptions(), Token)),
                    $"the action {action.Name}");
            }

            // --- A delete by query: its count and delete in one round trip, since it names no ids a
            // validator could see (spec 0015 §10.2); a stack whose deletes are judged serves none ---
            if (Serves(StackOperations.DeleteByQuery))
            {
                TKey[] matched = [(await CreateNewAsync()).Id, (await CreateNewAsync()).Id];
                DeleteByQueryRequest request = new(ByIds(matched).Filters![0], null, 2);
                AssertTrips(1, await CountedAsync(writer, scope => Service(scope).DeleteByQueryAsync(request, Token)), "a delete by query of two");
            }

            // --- The deletes that name ids: each with its pre-check, behind a validation round when judged ---
            int deleteBudget = ValidatesDeletes ? 2 : 1;
            if (Serves(StackOperations.Delete, Tree))
            {
                TEntity[] parents = [await CreateNewAsync(), await CreateNewAsync()];
                foreach (TEntity parent in parents)
                {
                    _ = await CreateAsync(NewChildOf(parent, NextOrdinal()));
                }

                AssertTrips(deleteBudget, await CountedAsync(writer, scope => Service(scope).Pipeline.DeleteWithDescendantsAsync([parents[0].Id, parents[1].Id], Token)), "a delete with descendants of two");
            }

            if (Serves(StackOperations.Delete))
            {
                AssertTrips(deleteBudget, await CountedAsync(writer, scope => Service(scope).DeleteByIdsAsync(ids, null, Token)), "a delete of two");
            }
        }

        /// <summary>
        ///     Asserts each child collection of a row holds two rows or more, at every depth, so a
        ///     budget measured over the row sees a statement issued per child row.
        /// </summary>
        /// <param name="entity">The row, as given or as loaded.</param>
        private void AssertTwoRowsAtEveryDepth(TEntity entity)
        {
            Check(Metadata, entity, string.Empty);

            void Check(EntityMetadata metadata, object row, string at)
            {
                foreach (ChildCollectionMetadata collection in metadata.Children)
                {
                    IReadOnlyList<object> rows = collection.Getter(row) ?? [];
                    Assert.True(rows.Count >= 2, $"{nameof(NewValidEntity)} gives the stack {Stack.Resource} {rows.Count} {at}{collection.Property}: fill each collection at every depth with at least two rows, so a statement per row shows in the round-trip budget.");
                    for (int index = 0; index < rows.Count; index++)
                    {
                        Check(collection.Child, rows[index], $"{at}{collection.Property}[{index}].");
                    }
                }
            }
        }

        /// <summary>
        ///     The round trips a create of entities takes: their persist, behind one validation round
        ///     when the pipeline or a validator has anything to load, and the validators' own rounds.
        /// </summary>
        /// <param name="entities">The entities to create.</param>
        /// <returns>The budget.</returns>
        private int CreateBudget(IEnumerable<TEntity> entities)
        {
            return 1 + (ValidatorsLoad || entities.Any(entity => Loads(Metadata, entity)) ? 1 : 0) + DependentValidationRounds;
        }

        /// <summary>
        ///     Whether a create of a row, or of any row its collections hold, has anything for the
        ///     pipeline to load: a unique value to probe, or a reference the client sets to resolve.
        /// </summary>
        /// <param name="entity">The row's metadata.</param>
        /// <param name="row">The row.</param>
        /// <param name="parentKey">The row's parent key, which its nesting sets; null for a root.</param>
        /// <returns>True when it loads.</returns>
        private static bool Loads(EntityMetadata entity, object row, PropertyMetadata? parentKey = null)
        {
            // --- The values a client writes: neither the key, a blob column nor a server-owned one ---
            HashSet<PropertyMetadata> blobs = new(entity.BlobReferences.Select(static blob => blob.Property), ReferenceEqualityComparer.Instance);
            bool Written(PropertyMetadata property)
            {
                return !ReferenceEquals(property, entity.Key)
                    && !ReferenceEquals(property, parentKey)
                    && !blobs.Contains(property)
                    && property.Ownership is PropertyOwnership.Editable or PropertyOwnership.WriteOnce
                    && property.Getter(row) is not null and not 0 and not 0L;
            }

            if (entity.Properties.Any(property => property.IsUnique && Written(property))
                || entity.References.Any(reference => Written(reference.ForeignKey)))
            {
                return true;
            }

            // --- The rows its collections hold, each under its own parent key ---
            return entity.Children.Any(collection => collection.Getter(row)?.Any(child => Loads(collection.Child, child, collection.ParentKey)) == true);
        }

        /// <summary>
        ///     Whether an import of a row, or of any row its collections hold, looks a reference up:
        ///     a reference the client writes holds a value, other than a row of the stack that the
        ///     same file holds, which the import binds within the file.
        /// </summary>
        /// <param name="entity">The row's metadata.</param>
        /// <param name="row">The row.</param>
        /// <param name="parentKey">The row's parent key, which its nesting sets; null for a root.</param>
        /// <param name="imported">The ids of the stack's rows the file holds.</param>
        /// <returns>True when it does.</returns>
        private bool LooksUp(EntityMetadata entity, object row, PropertyMetadata? parentKey, HashSet<long> imported)
        {
            // --- The references a client writes, each with a value the file does not hold itself ---
            foreach (ReferenceMetadata reference in entity.References)
            {
                PropertyMetadata key = reference.ForeignKey;
                if (ReferenceEquals(key, parentKey)
                    || key.Ownership is not (PropertyOwnership.Editable or PropertyOwnership.WriteOnce)
                    || entity.BlobReferences.Any(blob => ReferenceEquals(blob.Property, key))
                    || key.Getter(row) is not object value)
                {
                    continue;
                }

                bool inFile = reference.Target.ClrType == Metadata.ClrType && imported.Contains(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                if (!inFile)
                {
                    return true;
                }
            }

            // --- The rows its collections hold, each under its own parent key ---
            return entity.Children.Any(collection => collection.Getter(row)?.Any(child => LooksUp(collection.Child, child, collection.ParentKey, imported)) == true);
        }

        /// <summary>Updates rows by id through the stack's import action, from a staged editable export.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="import">The stack's import action.</param>
        /// <param name="fileId">The staged upload's id.</param>
        /// <returns>The parent rows the import updated.</returns>
        private static async Task<int> UpdateByImportAsync(IServiceProvider scope, ApiActionDescriptor import, int fileId)
        {
            ImportRequest request = new() { FileId = fileId, Mode = ImportMode.Update, RowKey = "Id" };
            object? result = await scope.GetRequiredService<IApiActionInvoker>().InvokeAsync(import, request, Token);
            return Assert.IsType<ImportOutcome>(result).Updated;
        }

        /// <summary>Invokes one of the stack's exports through the action invoker and disposes the workbook it answers.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="action">The stack's export or export-for-import action.</param>
        /// <param name="request">The action's request.</param>
        /// <returns>A task that completes when the workbook is written and disposed.</returns>
        private static async Task ExportAsync(IServiceProvider scope, ApiActionDescriptor action, object request)
        {
            object? result = await scope.GetRequiredService<IApiActionInvoker>().InvokeAsync(action, request, Token);
            await using ExportOutcome outcome = Assert.IsType<ExportOutcome>(result);
        }

        /// <summary>Writes an editable export of some rows and stages it as the caller's upload of an import file.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="editable">The stack's export-for-import action.</param>
        /// <param name="ids">The rows.</param>
        /// <returns>The staged upload's id.</returns>
        private static async Task<int> StageEditableExportAsync(IServiceProvider scope, ApiActionDescriptor editable, TKey[] ids)
        {
            ExportForImportRequest request = new() { Ids = [.. ids.Select(static id => long.CreateChecked(id))] };
            object? result = await scope.GetRequiredService<IApiActionInvoker>().InvokeAsync(editable, request, Token);
            await using ExportOutcome outcome = Assert.IsType<ExportOutcome>(result);
            using MemoryStream workbook = new();
            await outcome.Workbook.CopyToAsync(workbook, Token);
            workbook.Position = 0;
            BlobStageRequest stage = new(Import.FileKind, workbook, workbook.Length, BlobContentTypes.Xlsx, outcome.FileName);
            return (await scope.GetRequiredService<IBlobService>().StageAsync(stage, Token)).Id;
        }
    }
}
