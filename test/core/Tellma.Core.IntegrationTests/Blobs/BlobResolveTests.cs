// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.IntegrationTests.Blobs
{
    /// <summary>
    ///     Download resolution against a real tenant database, on one read round trip: a committed
    ///     blob is served through a filtered read of its owner and hidden when the filter leaves the
    ///     owner out, a child's blob through the parent's filtered read; a staged blob is served to its
    ///     uploader alone; an unrestricted read serves every owner's blob and no read serves none; a kind
    ///     any member reads is served without a read of its owner, its staged blobs still to their
    ///     uploaders alone; and
    ///     another kind, an unknown kind and a malformed variant are refused before anything is read.
    /// </summary>
    /// <param name="fixture">The tenant databases and the seeding helpers.</param>
    [Trait("Category", "Integration")]
    public sealed class BlobResolveTests(BlobReferenceDatabase fixture) : IClassFixture<BlobReferenceDatabase>
    {
        /// <summary>The blob owners' resource.</summary>
        private const string Owners = "fixture.BlobOwner";

        /// <summary>The filter of the read grant: the owners named <c>shown</c>.</summary>
        private const string ShownOnly = "Name = 'shown'";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_committed_blob_is_served_through_a_filtered_read_of_its_owner_on_one_round_trip()
        {
            (_, string subject) = await fixture.SeedCallerAsync(ConnectDatabase.TenantA, (Owners, AccessActions.Read, ShownOnly));
            (int shown, int hidden, _, _) = await SeedOwnersAsync();

            // --- Warm the caller, then the visible owner's blob on one round trip ---
            _ = await ResolveAsync(subject, BlobReferenceDatabase.FileKind, shown);
            (BlobDownload? served, int trips) = await CountedResolveAsync(subject, BlobReferenceDatabase.FileKind, shown);

            BlobDownload download = Assert.IsType<BlobDownload>(served);
            Assert.Equal((shown, BlobReferenceDatabase.FileKind, BlobState.Committed, BlobContentTypes.Pdf, 10L, "file.pdf", false), (download.Id, download.Kind, download.State, download.ContentType, download.Size, download.FileName, download.IsImage));
            Assert.Equal(await StorageKeyAsync(shown), download.StorageName);
            Assert.Equal(1, trips);

            // --- The hidden owner's blob is not found, on the same one round trip ---
            (BlobDownload? none, int hiddenTrips) = await CountedResolveAsync(subject, BlobReferenceDatabase.FileKind, hidden);
            Assert.Equal((null, 1), (none, hiddenTrips));
        }

        [Fact]
        public async Task A_childs_blob_is_served_through_the_parents_filtered_read()
        {
            (_, string subject) = await fixture.SeedCallerAsync(ConnectDatabase.TenantA, (Owners, AccessActions.Read, ShownOnly));
            (_, _, int shownChild, int hiddenChild) = await SeedOwnersAsync();

            Assert.NotNull(await ResolveAsync(subject, BlobReferenceDatabase.ChildFileKind, shownChild));
            Assert.Null(await ResolveAsync(subject, BlobReferenceDatabase.ChildFileKind, hiddenChild));
        }

        [Fact]
        public async Task A_staged_blob_is_served_to_its_uploader_alone_owner_or_not()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync(ConnectDatabase.TenantA, (Owners, AccessActions.Read, ShownOnly));
            (int otherId, string other) = await fixture.SeedCallerAsync(ConnectDatabase.TenantA, (Owners, AccessActions.Read, null));
            int mine = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            int theirs = await fixture.StageAsync(otherId, BlobReferenceDatabase.FileKind);
            int expired = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind, "DATEADD(minute, -1, SYSUTCDATETIME())");

            BlobDownload staged = Assert.IsType<BlobDownload>(await ResolveAsync(subject, BlobReferenceDatabase.FileKind, mine));
            Assert.Equal(BlobState.Staged, staged.State);
            Assert.Null(await ResolveAsync(subject, BlobReferenceDatabase.FileKind, theirs));
            Assert.Null(await ResolveAsync(subject, BlobReferenceDatabase.FileKind, expired));

            // --- Even an unrestricted reader of every owner is not served another user's staged blob ---
            Assert.Null(await ResolveAsync(other, BlobReferenceDatabase.FileKind, mine));
        }

        [Fact]
        public async Task An_unrestricted_read_serves_every_owners_blob_and_no_read_serves_none()
        {
            (_, string unrestricted) = await fixture.SeedCallerAsync(ConnectDatabase.TenantA, (Owners, AccessActions.Read, null));
            // A write implies the read, so the caller who reads no owner holds a grant on another resource alone.
            (_, string denied) = await fixture.SeedCallerAsync(ConnectDatabase.TenantA, ("fixture.Lookup", AccessActions.Read, null));
            (int shown, int hidden, _, int hiddenChild) = await SeedOwnersAsync();

            Assert.NotNull(await ResolveAsync(unrestricted, BlobReferenceDatabase.FileKind, hidden));
            Assert.NotNull(await ResolveAsync(unrestricted, BlobReferenceDatabase.ChildFileKind, hiddenChild));

            // --- No read: the owner is never read and the committed blob never served, on the blob row's round trip alone ---
            _ = await ResolveAsync(denied, BlobReferenceDatabase.FileKind, shown);
            (BlobDownload? none, int trips) = await CountedResolveAsync(denied, BlobReferenceDatabase.FileKind, shown);
            Assert.Equal((null, 1), (none, trips));
        }

        [Fact]
        public async Task A_kind_any_member_reads_is_served_without_a_read_of_its_owner_on_one_round_trip()
        {
            // --- An instance whose file kind any member reads, and a member with no grant on any owner ---
            await using ServiceProvider instance = fixture.Connect.CreateInstance(
                configure: static services => services.AddInMemoryBlobs(),
                compose: static tellma => tellma.Blobs(static blobs => blobs.Kind(BlobReferenceDatabase.FileKind, static policy => policy with { ReadAccess = BlobReadAccess.AnyMember })));
            (int memberId, string member) = await fixture.SeedCallerAsync(ConnectDatabase.TenantA, ("fixture.Lookup", AccessActions.Read, null));
            (int otherId, _) = await fixture.SeedCallerAsync(ConnectDatabase.TenantA, ("fixture.Lookup", AccessActions.Read, null));
            (_, int hidden, _, _) = await SeedOwnersAsync();
            int mine = await fixture.StageAsync(memberId, BlobReferenceDatabase.FileKind);
            int theirs = await fixture.StageAsync(otherId, BlobReferenceDatabase.FileKind);

            // --- A committed blob under an owner the member cannot read, on one round trip once warm ---
            _ = await ResolveAsync(member, BlobReferenceDatabase.FileKind, hidden, services: instance);
            (BlobDownload? served, int trips) = await CountedResolveAsync(member, BlobReferenceDatabase.FileKind, hidden, instance);
            Assert.Equal((hidden, BlobState.Committed, 1), (Assert.IsType<BlobDownload>(served).Id, served.State, trips));

            // --- Staged blobs: the member's own served, another user's still not ---
            Assert.NotNull(await ResolveAsync(member, BlobReferenceDatabase.FileKind, mine, services: instance));
            Assert.Null(await ResolveAsync(member, BlobReferenceDatabase.FileKind, theirs, services: instance));
        }

        [Fact]
        public async Task Another_kind_or_an_unknown_one_is_not_found_and_a_malformed_variant_is_a_bad_request()
        {
            (_, string subject) = await fixture.SeedCallerAsync(ConnectDatabase.TenantA, (Owners, AccessActions.Read, null));
            (int shown, _, _, _) = await SeedOwnersAsync();

            Assert.Null(await ResolveAsync(subject, BlobReferenceDatabase.PhotoKind, shown));
            Assert.Null(await ResolveAsync(subject, "fixture-unknown", shown));
            Assert.Null(await ResolveAsync(subject, BlobReferenceDatabase.FileKind, shown, "thumb"));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await Assert.ThrowsAsync<BadRequestException>(() => scope.ServiceProvider.GetRequiredService<IBlobService>().ResolveAsync(BlobReferenceDatabase.FileKind, shown, "../thumb", Token));
        }

        /// <summary>
        ///     Seeds an owner named <c>shown</c> and one named <c>hidden</c>, each holding a committed
        ///     file and a child holding a committed child file.
        /// </summary>
        /// <returns>The shown and the hidden owners' files, then the shown and the hidden children's files.</returns>
        private async Task<(int Shown, int Hidden, int ShownChild, int HiddenChild)> SeedOwnersAsync()
        {
            int shown = await fixture.CommitAsync(BlobReferenceDatabase.FileKind);
            int hidden = await fixture.CommitAsync(BlobReferenceDatabase.FileKind);
            int shownChild = await fixture.CommitAsync(BlobReferenceDatabase.ChildFileKind);
            int hiddenChild = await fixture.CommitAsync(BlobReferenceDatabase.ChildFileKind);

            // The owners' ids follow the blobs', so each seed's rows are its own.
            await fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped("[fixture].[BlobOwners]", "[Id], [Name], [FileId]", Invariant($"({shown}, N'shown', {shown})"), Invariant($"({hidden}, N'hidden', {hidden})")),
                Invariant($"INSERT INTO [fixture].[BlobOwnerFiles] ([Id], [BlobOwnerId], [FileId]) VALUES ({shownChild}, {shown}, {shownChild}), ({hiddenChild}, {hidden}, {hiddenChild});"));
            return (shown, hidden, shownChild, hiddenChild);
        }

        /// <summary>Resolves a download in a scope of its own.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="id">The blob's id.</param>
        /// <param name="variant">The variant; null for the primary.</param>
        /// <param name="services">The instance; the fixture's when null.</param>
        /// <returns>The download; null when nothing is served.</returns>
        private async Task<BlobDownload?> ResolveAsync(string subject, string kind, int id, string? variant = null, IServiceProvider? services = null)
        {
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, services: services ?? fixture.Instance);
            return await scope.ServiceProvider.GetRequiredService<IBlobService>().ResolveAsync(kind, id, variant, Token);
        }

        /// <summary>Resolves a download in a scope of its own, counting the round trips it took.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="id">The blob's id.</param>
        /// <param name="services">The instance; the fixture's when null.</param>
        /// <returns>The download, null when nothing is served, and the round trips.</returns>
        private async Task<(BlobDownload? Download, int Trips)> CountedResolveAsync(string subject, string kind, int id, IServiceProvider? services = null)
        {
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, services: services ?? fixture.Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            BlobDownload? download = await scope.ServiceProvider.GetRequiredService<IBlobService>().ResolveAsync(kind, id, null, Token);
            return (download, access.RoundTrips - before);
        }

        /// <summary>A blob's storage key.</summary>
        /// <param name="id">The blob's id.</param>
        /// <returns>The key.</returns>
        private Task<string?> StorageKeyAsync(int id)
        {
            return fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, Invariant($"SELECT [StorageKey] FROM [core].[Blobs] WHERE [Id] = {id};"));
        }

        /// <summary>Formats text in the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Invariant(FormattableString text)
        {
            return FormattableString.Invariant(text);
        }
    }
}
