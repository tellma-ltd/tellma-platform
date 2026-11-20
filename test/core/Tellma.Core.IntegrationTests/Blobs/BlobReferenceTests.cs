// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Data.Ids;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Blobs
{
    /// <summary>
    ///     The blob-reference rule and its statements against a real tenant database: a save confirms
    ///     the staged blobs it attaches and releases the ones it replaces or clears, on the root and on
    ///     child rows, an override save releasing what the row actually held; every shape of delete
    ///     releases its rows' blobs, the children's with the root's; an action's marked rows confirm,
    ///     release and refuse as a save's do; the validator refuses a missing,
    ///     committed, foreign, expired or repeated blob at its path and counts each refusal; the
    ///     persist refuses a blob that stopped being attachable after the validation round, or that a
    ///     second owner took, rolling the save back; a blob column is never read as a reference; and
    ///     a save adds no round trip for the blobs it attaches, replaces or clears whenever it runs a
    ///     first round, a cold id buffer included, while a warm-buffer insert that runs none takes one.
    /// </summary>
    /// <param name="fixture">The tenant databases and the seeding helpers.</param>
    [Trait("Category", "Integration")]
    public sealed class BlobReferenceTests(BlobReferenceDatabase fixture) : IClassFixture<BlobReferenceDatabase>
    {
        /// <summary>The blob owners' resource.</summary>
        private const string Owners = "fixture.BlobOwner";

        /// <summary>The blob folders' resource, a tree.</summary>
        private const string Folders = "fixture.BlobFolder";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task An_insert_confirms_what_it_attaches_an_update_releases_what_it_replaces_and_a_cleared_value_is_released()
        {
            (int userId, string subject) = await CallerAsync();

            // --- Attach on insert ---
            int first = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            BlobOwner owner = await SaveAsync(subject, new BlobOwner { Name = "attach", FileId = first });
            Assert.Equal("Committed|c|-", await fixture.StateAsync(first));
            Assert.Equal(first, await FileIdAsync(owner.Id));

            // --- Replace on update: the new one confirmed, the old one released for the sweep ---
            int second = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            owner.FileId = second;
            owner = await SaveAsync(subject, owner);
            Assert.Equal(("Released|c|e", "Committed|c|-"), (await fixture.StateAsync(first), await fixture.StateAsync(second)));

            // --- An unchanged value is left alone ---
            owner.Name = "renamed";
            owner = await SaveAsync(subject, owner);
            Assert.Equal("Committed|c|-", await fixture.StateAsync(second));

            // --- A cleared value is released ---
            owner.FileId = null;
            _ = await SaveAsync(subject, owner);
            Assert.Equal("Released|c|e", await fixture.StateAsync(second));
            Assert.Null(await FileIdAsync(owner.Id));
        }

        [Fact]
        public async Task A_save_attaching_both_columns_of_a_row_confirms_each_once()
        {
            (int userId, string subject) = await CallerAsync();
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            int photo = await fixture.StageAsync(userId, BlobReferenceDatabase.PhotoKind);

            BlobOwner owner = await SaveAsync(subject, new BlobOwner { Name = "both", FileId = file, PhotoId = photo });

            Assert.Equal(("Committed|c|-", "Committed|c|-"), (await fixture.StateAsync(file), await fixture.StateAsync(photo)));
            Assert.Equal((file, photo), (owner.FileId, owner.PhotoId));

            // --- Swapping the two values between columns is a release and a confirm of each: neither is staged, both refused ---
            (owner.FileId, owner.PhotoId) = (photo, file);
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync(subject, owner));
            Assert.Equal(["[0].FileId Blob.NotAttachable not-staged", "[0].PhotoId Blob.NotAttachable not-staged"], Lines(refused));
        }

        [Fact]
        public async Task A_child_row_attaches_on_insert_releases_on_replace_and_on_removal()
        {
            (int userId, string subject) = await CallerAsync();
            int first = await fixture.StageAsync(userId, BlobReferenceDatabase.ChildFileKind);
            int kept = await fixture.StageAsync(userId, BlobReferenceDatabase.ChildFileKind);

            // --- Attach on new child rows ---
            BlobOwner owner = await SaveAsync(subject, new BlobOwner { Name = "children", Files = [new BlobOwnerFile { FileId = first }, new BlobOwnerFile { FileId = kept }] });
            Assert.Equal(("Committed|c|-", "Committed|c|-"), (await fixture.StateAsync(first), await fixture.StateAsync(kept)));

            // --- Replace on a child row ---
            int second = await fixture.StageAsync(userId, BlobReferenceDatabase.ChildFileKind);
            owner = await DetailsAsync(subject, owner.Id);
            BlobOwnerFile replaced = Assert.Single(owner.Files!, file => file.FileId == first);
            replaced.FileId = second;
            owner = await SaveAsync(subject, owner);
            Assert.Equal(("Released|c|e", "Committed|c|-", "Committed|c|-"), (await fixture.StateAsync(first), await fixture.StateAsync(second), await fixture.StateAsync(kept)));

            // --- A child row removed from the collection releases its blob ---
            owner = await DetailsAsync(subject, owner.Id);
            owner.Files = [.. owner.Files!.Where(file => file.FileId == kept)];
            _ = await SaveAsync(subject, owner);
            Assert.Equal(("Released|c|e", "Committed|c|-"), (await fixture.StateAsync(second), await fixture.StateAsync(kept)));
        }

        [Fact]
        public async Task An_override_save_releases_the_value_the_row_actually_held_not_the_one_it_loaded()
        {
            (int userId, string subject) = await CallerAsync();
            int loaded = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            BlobOwner owner = await SaveAsync(subject, new BlobOwner { Name = "override", FileId = loaded });

            // --- Another caller commits a different blob onto the row while the save is composed ---
            int theirs = await fixture.CommitAsync(BlobReferenceDatabase.FileKind);
            int mine = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            owner.FileId = mine;
            SaveOptions options = new()
            {
                Concurrency = ConcurrencyMode.Override,
                ReturnEntities = false,
                OnPersist = Meanwhile(Invariant($"UPDATE [fixture].[BlobOwners] SET [FileId] = {theirs} WHERE [Id] = {owner.Id}; UPDATE [core].[Blobs] SET [State] = 'Released', [ExpiresAt] = SYSUTCDATETIME() WHERE [Id] = {loaded};")),
            };
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                _ = await Service(scope).SaveAsync([owner], options, Token);
            }

            // --- The row's value at write time is the one released ---
            Assert.Equal(("Released|c|e", "Committed|c|-"), (await fixture.StateAsync(theirs), await fixture.StateAsync(mine)));
            Assert.Equal(mine, await FileIdAsync(owner.Id));
        }

        [Fact]
        public async Task A_delete_by_ids_or_by_query_releases_its_rows_blobs_and_their_childrens()
        {
            (int userId, string subject) = await CallerAsync((Owners, AccessActions.Delete, null));

            // --- By ids: the root's blob and its children's ---
            int root = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            int child = await fixture.StageAsync(userId, BlobReferenceDatabase.ChildFileKind);
            BlobOwner byIds = await SaveAsync(subject, new BlobOwner { Name = "by-ids", FileId = root, Files = [new BlobOwnerFile { FileId = child }] });
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                _ = await Service(scope).DeleteByIdsAsync([byIds.Id], null, Token);
            }

            Assert.Equal(("Released|c|e", "Released|c|e"), (await fixture.StateAsync(root), await fixture.StateAsync(child)));

            // --- By query: every matched row's blobs ---
            string name = "q-" + Guid.NewGuid().ToString("N")[..12];
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            int photo = await fixture.StageAsync(userId, BlobReferenceDatabase.PhotoKind);
            _ = await SaveAsync(subject, new BlobOwner { Name = name, FileId = file });
            _ = await SaveAsync(subject, new BlobOwner { Name = name, PhotoId = photo });
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                _ = await Service(scope).DeleteByQueryAsync(new DeleteByQueryRequest($"Name = '{name}'", null, 2), Token);
            }

            Assert.Equal(("Released|c|e", "Released|c|e"), (await fixture.StateAsync(file), await fixture.StateAsync(photo)));
        }

        [Fact]
        public async Task A_delete_with_descendants_releases_every_blob_of_the_subtree()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync(
                ConnectDatabase.TenantA, (Folders, AccessActions.Read, null), (Folders, AccessActions.Save, null), (Folders, AccessActions.Delete, null));
            int top = await fixture.StageAsync(userId, BlobReferenceDatabase.FolderIconKind);
            int nested = await fixture.StageAsync(userId, BlobReferenceDatabase.FolderIconKind);
            int beside = await fixture.StageAsync(userId, BlobReferenceDatabase.FolderIconKind);

            // --- A folder with a subfolder, and a folder beside them ---
            int parentId;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                EntityService<BlobFolder, int> folders = scope.ServiceProvider.GetRequiredService<EntityService<BlobFolder, int>>();
                parentId = (int)Assert.Single((await folders.SaveAsync([new BlobFolder { Name = "top", IconId = top }], new SaveOptions { ReturnEntities = false }, Token)).Ids);
                _ = await folders.SaveAsync(
                    [new BlobFolder { Name = "nested", IconId = nested, ParentId = parentId }, new BlobFolder { Name = "beside", IconId = beside }],
                    new SaveOptions { ReturnEntities = false },
                    Token);
            }

            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                _ = await scope.ServiceProvider.GetRequiredService<EntityService<BlobFolder, int>>().DeleteWithDescendantsAsync([parentId], Token);
            }

            Assert.Equal(
                ("Released|c|e", "Released|c|e", "Committed|c|-"),
                (await fixture.StateAsync(top), await fixture.StateAsync(nested), await fixture.StateAsync(beside)));
        }

        [Fact]
        public async Task The_validator_refuses_each_unattachable_blob_at_its_path_and_counts_it_by_kind_and_reason()
        {
            (int userId, string subject) = await CallerAsync();
            (int otherId, _) = await CallerAsync();
            int expired = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind, "DATEADD(minute, -1, SYSUTCDATETIME())");
            int foreign = await fixture.StageAsync(otherId, BlobReferenceDatabase.PhotoKind);
            int committed = await fixture.CommitAsync(BlobReferenceDatabase.FileKind);
            int otherKind = await fixture.StageAsync(userId, BlobReferenceDatabase.PhotoKind);
            int repeated = await fixture.StageAsync(userId, BlobReferenceDatabase.ChildFileKind);
            using MetricCollector<long> rejected = Collector();

            BlobOwner[] payload =
            [
                new() { Name = "expired", FileId = expired, PhotoId = foreign },
                new() { Name = "committed", FileId = committed },
                new() { Name = "kind", FileId = otherKind, PhotoId = int.MaxValue },
                new() { Name = "repeated", Files = [new BlobOwnerFile { FileId = repeated }, new BlobOwnerFile { FileId = repeated }] },
            ];
            ValidationException refused;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                refused = await Assert.ThrowsAsync<ValidationException>(() => Service(scope).SaveAsync(payload, new SaveOptions(), Token));
            }

            Assert.Equal(
                [
                    "[0].FileId Blob.NotAttachable expired",
                    "[0].PhotoId Blob.NotAttachable uploader",
                    "[1].FileId Blob.NotAttachable not-staged",
                    "[2].FileId Blob.NotAttachable kind",
                    "[2].PhotoId Blob.NotAttachable missing",
                    "[3].Files[1].FileId Blob.DuplicateReference -",
                ],
                Lines(refused));
            Assert.Equal(
                [
                    "fixture-child-file duplicate",
                    "fixture-file expired",
                    "fixture-file kind",
                    "fixture-file not-staged",
                    "fixture-photo missing",
                    "fixture-photo uploader",
                ],
                Counted(rejected));

            // --- Nothing the save attached was confirmed ---
            Assert.Equal(("Staged|-|e", "Staged|-|e"), (await fixture.StateAsync(otherKind), await fixture.StateAsync(repeated)));
        }

        [Fact]
        public async Task A_blob_that_expires_after_the_validation_round_is_refused_by_the_persist_and_the_save_rolls_back()
        {
            (int userId, string subject) = await CallerAsync();
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            int photo = await fixture.StageAsync(userId, BlobReferenceDatabase.PhotoKind);
            using MetricCollector<long> rejected = Collector();

            // --- The file expires on another connection between the validation round and the persist ---
            SaveOptions options = new() { OnPersist = Meanwhile(Invariant($"UPDATE [core].[Blobs] SET [ExpiresAt] = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE [Id] = {file};")) };
            ValidationException refused;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                refused = await Assert.ThrowsAsync<ValidationException>(() => Service(scope).SaveAsync([new BlobOwner { Name = "late", FileId = file, PhotoId = photo }], options, Token));
            }

            Assert.Equal([" Blob.NotAttachable persist"], Lines(refused));
            Assert.Equal(["fixture-file persist", "fixture-photo persist"], Counted(rejected));

            // --- The photo's confirm rolled back with the save ---
            Assert.Equal(("Staged|-|e", "Staged|-|e"), (await fixture.StateAsync(file), await fixture.StateAsync(photo)));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[BlobOwners] WHERE [Name] = N'late';"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_blob_handed_to_another_uploader_or_kind_after_the_validation_round_is_refused_by_the_persist_and_stays_staged(bool kind)
        {
            (int userId, string subject) = await CallerAsync();
            (int otherId, _) = await CallerAsync();
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            using MetricCollector<long> rejected = Collector();

            // --- On another connection, between the validation round and the persist, the staged row
            // changes uploader or kind: the validator passed it, so only the confirm's own conditions can refuse it ---
            string change = kind ? Invariant($"[Kind] = N'{BlobReferenceDatabase.PhotoKind}'") : Invariant($"[CreatedById] = {otherId}");
            SaveOptions options = new() { OnPersist = Meanwhile(Invariant($"UPDATE [core].[Blobs] SET {change} WHERE [Id] = {file};")) };
            ValidationException refused;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                refused = await Assert.ThrowsAsync<ValidationException>(() => Service(scope).SaveAsync([new BlobOwner { Name = "handed", FileId = file }], options, Token));
            }

            Assert.Equal([" Blob.NotAttachable persist"], Lines(refused));
            Assert.Equal(["fixture-file persist"], Counted(rejected));

            // --- Nothing was confirmed and no owner row was written ---
            Assert.Equal("Staged|-|e", await fixture.StateAsync(file));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[BlobOwners] WHERE [Name] = N'handed';"));
        }

        [Fact]
        public async Task A_blob_a_second_owner_takes_before_the_commit_is_refused_by_the_unique_index_as_not_attachable()
        {
            (int userId, string subject) = await CallerAsync();
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            using MetricCollector<long> rejected = Collector();

            // --- Another owner row takes the blob on another connection between the validation round and the persist ---
            SaveOptions options = new() { OnPersist = Meanwhile(DataQueryDatabase.Stamped("[fixture].[BlobOwners]", "[Id], [Name], [FileId]", Invariant($"(900, N'theirs', {file})"))) };
            ValidationException refused;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                refused = await Assert.ThrowsAsync<ValidationException>(() => Service(scope).SaveAsync([new BlobOwner { Name = "second", FileId = file }], options, Token));
            }

            Assert.Equal(["[0].FileId Blob.NotAttachable persist"], Lines(refused));
            Assert.Equal(["fixture-file persist"], Counted(rejected));
            Assert.Equal("Staged|-|e", await fixture.StateAsync(file));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[BlobOwners] WHERE [Name] = N'second';"));
        }

        [Fact]
        public async Task An_action_writing_a_blob_column_through_the_rows_it_marks_confirms_releases_and_clears_as_a_save_does()
        {
            (int userId, string subject) = await ActionCallerAsync();
            BlobOwner owner = await SaveAsync(subject, new BlobOwner { Name = "acted" });

            // --- Set: the staged blob is confirmed ---
            int first = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            await ActAsync(subject, BlobOwnerService.SetFile, owner.Id, new BlobOwnerSetFile { FileId = first });
            Assert.Equal("Committed|c|-", await fixture.StateAsync(first));
            Assert.Equal(first, await FileIdAsync(owner.Id));

            // --- Replace: the new one confirmed, the old one released for the sweep ---
            int second = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            await ActAsync(subject, BlobOwnerService.SetFile, owner.Id, new BlobOwnerSetFile { FileId = second });
            Assert.Equal(("Released|c|e", "Committed|c|-"), (await fixture.StateAsync(first), await fixture.StateAsync(second)));

            // --- Clear: released ---
            await ActAsync(subject, BlobOwnerService.SetFile, owner.Id, new BlobOwnerSetFile { FileId = null });
            Assert.Equal("Released|c|e", await fixture.StateAsync(second));
            Assert.Null(await FileIdAsync(owner.Id));
        }

        [Fact]
        public async Task An_action_dropping_a_child_row_through_the_rows_it_marks_releases_the_childs_blob()
        {
            (int userId, string subject) = await ActionCallerAsync();
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.ChildFileKind);
            BlobOwner owner = await SaveAsync(subject, new BlobOwner { Name = "dropped", Files = [new BlobOwnerFile { FileId = file }] });
            Assert.Equal("Committed|c|-", await fixture.StateAsync(file));

            await ActAsync(subject, BlobOwnerService.DropFiles, owner.Id, arguments: null);

            Assert.Equal("Released|c|e", await fixture.StateAsync(file));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, Invariant($"SELECT COUNT(*) FROM [fixture].[BlobOwnerFiles] WHERE [BlobOwnerId] = {owner.Id};")));
        }

        [Fact]
        public async Task An_action_attaching_another_users_staged_blob_is_refused_by_the_persist_and_writes_nothing()
        {
            (_, string subject) = await ActionCallerAsync();
            (int otherId, _) = await CallerAsync();
            int theirs = await fixture.StageAsync(otherId, BlobReferenceDatabase.FileKind);
            BlobOwner owner = await SaveAsync(subject, new BlobOwner { Name = "foreign" });

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                () => ActAsync(subject, BlobOwnerService.SetFile, owner.Id, new BlobOwnerSetFile { FileId = theirs }));

            Assert.Equal([" Blob.NotAttachable persist"], Lines(refused));
            Assert.Equal("Staged|-|e", await fixture.StateAsync(theirs));
            Assert.Null(await FileIdAsync(owner.Id));
        }

        [Fact]
        public async Task A_blob_column_is_never_read_as_a_reference_so_a_caller_with_no_grant_on_blobs_attaches_one()
        {
            // The caller holds no grant at all on core.Blob: a reference check would refuse every attach.
            (int userId, string subject) = await CallerAsync();
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);

            BlobOwner owner = await SaveAsync(subject, new BlobOwner { Name = "no-blob-grant", FileId = file });
            Assert.Equal(file, owner.FileId);

            // --- An id no row holds is the attach rule's refusal, never a missing reference ---
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => Service(scope).SaveAsync([new BlobOwner { Name = "missing", FileId = int.MaxValue - 1 }], new SaveOptions(), Token));
            Assert.Equal(["[0].FileId Blob.NotAttachable missing"], Lines(refused));
        }

        [Fact]
        public async Task A_save_that_replaces_or_clears_a_blob_takes_the_round_trips_of_one_that_changes_nothing_else()
        {
            (int userId, string subject) = await CallerAsync();
            int first = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            int second = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);

            // --- Warm the caller, the stack and its ids; then an insert without and with a blob ---
            _ = await SaveAsync(subject, new BlobOwner { Name = "warm" });
            (BlobOwner plain, int plainTrips) = await CountedSaveAsync(subject, new BlobOwner { Name = "plain" });
            (BlobOwner owner, int attachTrips) = await CountedSaveAsync(subject, new BlobOwner { Name = "attach", FileId = first });

            // --- Updates: a rename, a replace and a clear ride the before images' round trip ---
            plain.Name = "plain renamed";
            (_, int renameTrips) = await CountedSaveAsync(subject, plain);
            owner.FileId = second;
            (owner, int replaceTrips) = await CountedSaveAsync(subject, owner);
            owner.FileId = null;
            (_, int clearTrips) = await CountedSaveAsync(subject, owner);

            Assert.Equal((1, 2), (plainTrips, renameTrips));
            Assert.Equal((renameTrips, renameTrips), (replaceTrips, clearTrips));

            // A warm-buffer insert that runs no first round loads the blob rows it attaches on a
            // validation round of its own: the one shape that pays for the attach rule's load.
            Assert.Equal(plainTrips + 1, attachTrips);
            Assert.Equal(("Released|c|e", "Released|c|e"), (await fixture.StateAsync(first), await fixture.StateAsync(second)));
        }

        [Fact]
        public async Task With_a_cold_id_buffer_an_insert_or_an_update_adding_a_child_takes_no_round_trip_for_the_blobs_it_attaches()
        {
            (int userId, string subject) = await CallerAsync();
            _ = await SaveAsync(subject, new BlobOwner { Name = "warm" });

            // --- An insert: the ids and the blob rows ride one round trip, with or without a blob ---
            (_, int plainTrips) = await ColdAsync(subject, new BlobOwner { Name = "cold plain" });
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            (BlobOwner owner, int attachTrips) = await ColdAsync(subject, new BlobOwner { Name = "cold attach", FileId = file });
            Assert.Equal((2, 2), (plainTrips, attachTrips));

            // --- An update adding a child row: the before images, the ids and the blob rows ride one round trip ---
            BlobOwner bare = await SaveAsync(subject, new BlobOwner { Name = "cold children" });
            bare = await DetailsAsync(subject, bare.Id);
            bare.Files = [new BlobOwnerFile()];
            (_, int childTrips) = await ColdAsync(subject, bare);

            int childFile = await fixture.StageAsync(userId, BlobReferenceDatabase.ChildFileKind);
            int replacement = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            owner = await DetailsAsync(subject, owner.Id);
            owner.FileId = replacement;
            owner.Files = [new BlobOwnerFile { FileId = childFile }];
            (_, int childFileTrips) = await ColdAsync(subject, owner);
            Assert.Equal((2, 2), (childTrips, childFileTrips));

            Assert.Equal(
                ("Released|c|e", "Committed|c|-", "Committed|c|-"),
                (await fixture.StateAsync(file), await fixture.StateAsync(replacement), await fixture.StateAsync(childFile)));
        }

        /// <summary>Seeds a caller of the first tenant who reads and saves blob owners, with any further permissions.</summary>
        /// <param name="more">The further permissions.</param>
        /// <returns>The caller's id and subject.</returns>
        private Task<(int UserId, string Subject)> CallerAsync(params (string Resource, string Action, string? Filter)[] more)
        {
            return fixture.SeedCallerAsync(ConnectDatabase.TenantA, [(Owners, AccessActions.Read, null), (Owners, AccessActions.Save, null), .. more]);
        }

        /// <summary>Seeds a caller who reads and saves owners and runs both of the owner service's actions.</summary>
        /// <returns>The caller's id and subject.</returns>
        private Task<(int UserId, string Subject)> ActionCallerAsync()
        {
            // The securable actions are the PascalCase forms of the actions' names.
            return CallerAsync((Owners, "SetFile", null), (Owners, "DropFiles", null));
        }

        /// <summary>Runs an action of the owner service on one owner in a scope of its own.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="id">The owner's id.</param>
        /// <param name="arguments">The action's arguments; null for none.</param>
        /// <returns>A task that completes when the action committed.</returns>
        private async Task ActAsync(string subject, string action, int id, object? arguments)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await Service(scope).ExecuteActionAsync(action, [id], arguments, new ActionOptions(), Token);
        }

        /// <summary>Saves one owner in a scope of its own and returns the row it committed.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="owner">The owner.</param>
        /// <returns>The committed row, with its files.</returns>
        private async Task<BlobOwner> SaveAsync(string subject, BlobOwner owner)
        {
            return (await CountedSaveAsync(subject, owner)).Saved;
        }

        /// <summary>Saves one owner in a scope of its own, counting the round trips the save took.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="owner">The owner.</param>
        /// <returns>The committed row, with its files, and the round trips.</returns>
        private async Task<(BlobOwner Saved, int Trips)> CountedSaveAsync(string subject, BlobOwner owner)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            EntitiesResult<BlobOwner> saved = await Service(scope).SaveAsync([owner], new SaveOptions(), Token);
            return (Assert.Single(saved.Entities), access.RoundTrips - before);
        }

        /// <summary>Saves one owner with the tenant's id buffers dropped first, counting the round trips the save took.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="owner">The owner.</param>
        /// <returns>The committed row, with its files, and the round trips.</returns>
        private Task<(BlobOwner Saved, int Trips)> ColdAsync(string subject, BlobOwner owner)
        {
            // Every new row's id then rides round trip 1, and the validators start after it.
            fixture.Instance.GetRequiredService<IdAllocator>().ForgetTenant(ConnectDatabase.TenantA);
            return CountedSaveAsync(subject, owner);
        }

        /// <summary>Reads an owner in details shape, with its files.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="id">The owner's id.</param>
        /// <returns>The owner.</returns>
        private async Task<BlobOwner> DetailsAsync(string subject, int id)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            return Assert.Single((await Service(scope).GetByIdAsync(id, new DetailsRequest(), Token)).Entities);
        }

        /// <summary>An owner's stored file.</summary>
        /// <param name="id">The owner's id.</param>
        /// <returns>The file's id; null when the row holds none.</returns>
        private Task<int?> FileIdAsync(int id)
        {
            return fixture.Connect.ScalarAsync<int?>(ConnectDatabase.TenantA, Invariant($"SELECT [FileId] FROM [fixture].[BlobOwners] WHERE [Id] = {id};"));
        }

        /// <summary>
        ///     Another caller's commit in the window between a save's validation round trip and its
        ///     persist: the statement commits on a connection of its own while the persist is composed.
        /// </summary>
        /// <param name="statement">The other caller's statement.</param>
        /// <returns>The save's caller statements, which append nothing.</returns>
        private Action<IDataBatch> Meanwhile(string statement)
        {
            // The caller's statements are composed synchronously, after the validation round trip
            // completed and before the persist is sent; the other connection's commit is awaited off
            // the composing thread.
            return _ => Task.Run(() => fixture.Connect.ExecuteAsync(ConnectDatabase.TenantA, statement)).GetAwaiter().GetResult();
        }

        /// <summary>A collector of the attach refusals on the instance's meters.</summary>
        /// <returns>The collector.</returns>
        private MetricCollector<long> Collector()
        {
            return new MetricCollector<long>(fixture.Instance.GetRequiredService<IMeterFactory>(), BlobTelemetryNames.MeterName, BlobTelemetryNames.AttachRejected);
        }

        /// <summary>The refusals a collector counted, each as its kind and reason, ordered.</summary>
        /// <param name="collector">The collector.</param>
        /// <returns>The lines.</returns>
        private static string[] Counted(MetricCollector<long> collector)
        {
            return [.. collector.GetMeasurementSnapshot()
                .Select(static measurement => $"{measurement.Tags[BlobTelemetryNames.KindTag]} {measurement.Tags[BlobTelemetryNames.ReasonTag]}")
                .Order(StringComparer.Ordinal)];
        }

        /// <summary>A refusal's errors, each as its path, code and reason, ordered.</summary>
        /// <param name="refusal">The refusal.</param>
        /// <returns>The lines.</returns>
        private static string[] Lines(ValidationException refusal)
        {
            return [.. refusal.Errors
                .Select(static error => $"{error.Path} {error.Code} {(error.Arguments.TryGetValue("reason", out object? reason) ? reason : "-")}")
                .Order(StringComparer.Ordinal)];
        }

        /// <summary>Formats text in the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Invariant(FormattableString text)
        {
            return FormattableString.Invariant(text);
        }

        /// <summary>A scope's blob owner service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<BlobOwner, int> Service(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<BlobOwner, int>>();
        }
    }
}
