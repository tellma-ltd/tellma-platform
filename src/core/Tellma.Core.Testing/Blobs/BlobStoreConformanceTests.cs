// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Blobs;
using Xunit;

namespace Tellma.Core.Testing.Blobs
{
    /// <summary>
    ///     The executable form of the <see cref="IBlobStore" /> contract. Every blob store derives
    ///     from this and supplies a harness; the cases below are what the blob service, the sweep and
    ///     the tooling rely on, whichever store is behind the interface.
    /// </summary>
    /// <remarks>
    ///     Cases a store cannot express are skipped through <see cref="BlobStoreCapabilities" />, and
    ///     every skip reason names the store's limitation, so a skip reads as "not applicable",
    ///     never as "not implemented".
    /// </remarks>
    public abstract class BlobStoreConformanceTests
    {
        /// <summary>The kind every conformance object is written under.</summary>
        public const string Kind = "conformance";

        /// <summary>A second kind whose name starts with <see cref="Kind" />, for the prefix-isolation case.</summary>
        public const string LongerKind = "conformance-other";

        /// <summary>The media type conformance objects are written with.</summary>
        public const string ContentType = "application/octet-stream";

        /// <summary>Names outside the grammar, traversal attempts among them; every member must refuse each.</summary>
        /// <returns>The names.</returns>
        public static TheoryData<string> InvalidNames()
        {
            // A fixed key, so the theory's cases are the same at discovery and at execution.
            const string key = "ab0123456789abcdef0123456789abcd";
            return
            [
                "",
                "..",
                "../x",
                "../../t2/" + Kind + "/ab/" + key,
                Kind + "/ab/../../t2/" + Kind + "/ab/" + key,
                Kind + "/ab/" + key + "/..",
                Kind + "\\ab\\" + key,
                "/" + Kind + "/ab/" + key,
                "C:/" + Kind + "/ab/" + key,
                Kind + "/ab/" + key.ToUpperInvariant(),
                Kind + "/cd/" + key,
                Kind + "/ab/" + key + ".",
                Kind + "/ab/" + key + "..thumb",
                Kind + "/ab/" + key + ".thumb.x",
                Kind + "/ab/" + key + "/",
                Kind + "/ab/" + key[..31],
                Kind + "/ab/%2e%2e",
                Kind + "/ab/" + key + "\0",
                "con/ab/" + key,
                Kind + "/ab/" + key[..30] + "\uFF0E\uFF0E",
            ];
        }

        /// <summary>Creates the harness for one case.</summary>
        /// <returns>The harness; the suite disposes it.</returns>
        protected abstract ValueTask<IBlobStoreHarness> CreateHarnessAsync();

        /// <summary>A fresh primary name of <see cref="Kind" /> whose fan-out segment is <paramref name="fanOut" />.</summary>
        /// <param name="fanOut">Two lowercase hexadecimal digits, or null for a random key.</param>
        /// <param name="kind">The kind, or null for <see cref="Kind" />.</param>
        /// <returns>The name.</returns>
        public static string NewName(string? fanOut = null, string? kind = null)
        {
            return BlobName.Primary(kind ?? Kind, fanOut is null ? Guid.NewGuid().ToString("N") : KeyWithFanOut(fanOut));
        }

        /// <summary>A write of <paramref name="bytes" /> under <paramref name="name" />.</summary>
        /// <param name="name">The object's name.</param>
        /// <param name="bytes">The object's bytes.</param>
        /// <returns>The write; its content is a fresh stream over the bytes.</returns>
        public static BlobWrite Write(string name, byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return new BlobWrite(name, ContentType, new MemoryStream(bytes, writable: false), bytes.Length);
        }

        /// <summary>Bytes that identify their writer, so a read-back proves which write it saw.</summary>
        /// <param name="text">The identifying text.</param>
        /// <returns>The bytes.</returns>
        public static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        /// <summary>Contract: what was written reads back byte for byte, through a seekable stream of the object's length.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Writes_then_reads_back_the_same_bytes_through_a_seekable_stream_of_known_length()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string name = NewName();
            byte[] bytes = [.. Enumerable.Range(0, 100_000).Select(static i => (byte)(i % 251))];

            await harness.Store.WriteAsync(harness.TenantId, [Write(name, bytes)], Token);

            await using BlobContent? content = await harness.Store.OpenReadAsync(harness.TenantId, name, Token);
            Assert.NotNull(content);
            Assert.Equal(bytes.Length, content.Length);
            Assert.True(content.Content.CanSeek, "A blob read must be seekable, so a download can answer range requests.");
            Assert.Equal(bytes.Length, content.Content.Length);
            Assert.Equal(bytes, await ReadToEndAsync(content.Content));

            // Seek into the middle and read a range, as a range request would.
            content.Content.Seek(50_000, SeekOrigin.Begin);
            byte[] range = new byte[10];
            await content.Content.ReadExactlyAsync(range, Token);
            Assert.Equal(bytes.AsSpan(50_000, 10).ToArray(), range);
        }

        /// <summary>Contract: an object of zero bytes is an object.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task An_empty_object_round_trips()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string name = NewName();

            await harness.Store.WriteAsync(harness.TenantId, [Write(name, [])], Token);

            await using BlobContent? content = await harness.Store.OpenReadAsync(harness.TenantId, name, Token);
            Assert.NotNull(content);
            Assert.Equal(0, content.Length);
            Assert.Empty(await ReadToEndAsync(content.Content));
        }

        /// <summary>Contract: writes are create-only; a second write of a name throws and the first bytes stay.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task A_create_only_write_over_an_existing_name_throws_and_keeps_the_original()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string name = NewName();
            await harness.Store.WriteAsync(harness.TenantId, [Write(name, Bytes("original"))], Token);

            BlobAlreadyExistsException exception = await Assert.ThrowsAsync<BlobAlreadyExistsException>(
                () => harness.Store.WriteAsync(harness.TenantId, [Write(name, Bytes("replacement"))], Token));

            Assert.Equal(name, exception.Name);
            Assert.Equal(Bytes("original"), await ReadAsync(harness, harness.TenantId, name));
        }

        /// <summary>
        ///     Contract: create-only holds under contention. Writers of one name racing behind a
        ///     barrier leave exactly one winner; every other writer gets
        ///     <see cref="BlobAlreadyExistsException" />, the bytes read back are the winner's, and
        ///     nothing incomplete remains.
        /// </summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Concurrent_writers_of_one_name_leave_exactly_one_winner()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            const int Writers = 8;
            const int Rounds = 5;

            for (int round = 0; round < Rounds; round++)
            {
                string name = NewName();
                TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Task[] writes = [.. Enumerable.Range(0, Writers).Select(writer => Task.Run(async () =>
                {
                    BlobWrite write = Write(name, Bytes($"round {round} writer {writer}"));
                    await gate.Task;
                    await harness.Store.WriteAsync(harness.TenantId, [write], Token);
                }))];

                gate.SetResult();
                try
                {
                    await Task.WhenAll(writes);
                }
                catch (BlobAlreadyExistsException)
                {
                    // Inspected per writer below.
                }

                int winner = Assert.Single(Enumerable.Range(0, Writers), writer => writes[writer].IsCompletedSuccessfully);
                for (int writer = 0; writer < Writers; writer++)
                {
                    if (writer != winner)
                    {
                        BlobAlreadyExistsException conflict = Assert.IsType<BlobAlreadyExistsException>(writes[writer].Exception?.InnerException);
                        Assert.Equal(name, conflict.Name);
                    }
                }

                Assert.Equal(Bytes($"round {round} writer {winner}"), await ReadAsync(harness, harness.TenantId, name));
            }

            Assert.Equal(0, await harness.CountIncompleteArtefactsAsync(harness.TenantId));
        }

        /// <summary>Contract: a write is best-effort; a failing object leaves the call's other objects in place.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task A_failing_write_leaves_the_other_writes_of_the_call_in_place()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string existing = NewName();
            string fresh = NewName();
            await harness.Store.WriteAsync(harness.TenantId, [Write(existing, Bytes("first"))], Token);

            await Assert.ThrowsAsync<BlobAlreadyExistsException>(
                () => harness.Store.WriteAsync(harness.TenantId, [Write(existing, Bytes("second")), Write(fresh, Bytes("fresh"))], Token));

            Assert.Equal(Bytes("first"), await ReadAsync(harness, harness.TenantId, existing));
            Assert.Equal(Bytes("fresh"), await ReadAsync(harness, harness.TenantId, fresh));
        }

        /// <summary>Contract: content that does not yield exactly its declared length fails and leaves no object.</summary>
        /// <param name="declared">The declared length.</param>
        /// <param name="actual">The number of bytes the content yields.</param>
        /// <returns>A task that completes when the case has run.</returns>
        [Theory]
        [InlineData(10, 9)]
        [InlineData(10, 11)]
        [InlineData(0, 1)]
        [InlineData(200_000, 100_000)]
        public async Task Content_that_does_not_match_its_declared_length_fails_and_leaves_no_object(int declared, int actual)
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string name = NewName();
            BlobWrite write = new(name, ContentType, new MemoryStream(new byte[actual], writable: false), declared);

            await Assert.ThrowsAsync<BlobStoreException>(() => harness.Store.WriteAsync(harness.TenantId, [write], Token));

            Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, name, Token));
            Assert.Equal(0, await harness.CountIncompleteArtefactsAsync(harness.TenantId));
        }

        /// <summary>Contract: a primary and its variants are written in one call.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task A_primary_and_its_thumbnail_are_written_in_one_call()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string primary = NewName();
            string thumb = BlobName.Variant(primary, ThumbnailPolicy.VariantName);

            await harness.Store.WriteAsync(
                harness.TenantId, [Write(primary, Bytes("primary")), Write(thumb, Bytes("thumb"))], Token);

            Assert.Equal(Bytes("primary"), await ReadAsync(harness, harness.TenantId, primary));
            Assert.Equal(Bytes("thumb"), await ReadAsync(harness, harness.TenantId, thumb));
            Assert.Equal(0, await harness.CountIncompleteArtefactsAsync(harness.TenantId));
        }

        /// <summary>Contract: one name twice in one call is refused before anything is written.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task A_name_twice_in_one_write_is_refused_before_anything_is_written()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string name = NewName();

            await Assert.ThrowsAnyAsync<ArgumentException>(
                () => harness.Store.WriteAsync(harness.TenantId, [Write(name, Bytes("a")), Write(name, Bytes("b"))], Token));

            Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, name, Token));
        }

        /// <summary>Contract: an empty write list is a no-op.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task An_empty_write_is_a_no_op()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();

            await harness.Store.WriteAsync(harness.TenantId, [], Token);
            await harness.Store.DeleteAsync(harness.TenantId, [], Token);
        }

        /// <summary>Contract: reading an object that does not exist returns null.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Reading_a_missing_object_returns_null()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);

            Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, NewName(), Token));
            Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, BlobName.Variant(NewName(), ThumbnailPolicy.VariantName), Token));
        }

        /// <summary>Contract: reading in a tenant whose storage does not exist returns null and creates nothing.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Reading_from_a_tenant_without_storage_returns_null_and_creates_nothing()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.RemoveTenantStorageAsync(harness.TenantId);

            Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, NewName(), Token));
            Assert.False(await harness.TenantStorageExistsAsync(harness.TenantId));
        }

        /// <summary>Contract: an object written for one tenant is invisible to another.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Tenants_are_isolated()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string name = NewName("ab");
            await harness.Store.WriteAsync(harness.TenantId, [Write(name, Bytes("mine"))], Token);
            await harness.Store.EnsureTenantAsync(harness.OtherTenantId, Token);

            Assert.Null(await harness.Store.OpenReadAsync(harness.OtherTenantId, name, Token));
            Assert.Empty(await ListAsync(harness, harness.OtherTenantId, Kind));

            // A delete in the other tenant does not reach this one.
            await harness.Store.DeleteAsync(harness.OtherTenantId, [name], Token);
            Assert.Equal(Bytes("mine"), await ReadAsync(harness, harness.TenantId, name));
        }

        /// <summary>Contract: deleting removes every named object, primaries and variants alike.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Deleting_removes_every_named_object()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string[] names = [.. Enumerable.Range(0, 12).Select(static _ => NewName())];
            string thumb = BlobName.Variant(names[0], ThumbnailPolicy.VariantName);
            await harness.Store.WriteAsync(
                harness.TenantId, [.. names.Select(static name => Write(name, Bytes(name))), Write(thumb, Bytes(thumb))], Token);
            string survivor = NewName();
            await harness.Store.WriteAsync(harness.TenantId, [Write(survivor, Bytes(survivor))], Token);

            await harness.Store.DeleteAsync(harness.TenantId, [.. names, thumb], Token);

            foreach (string name in names.Append(thumb))
            {
                Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, name, Token));
            }

            Assert.Equal(Bytes(survivor), await ReadAsync(harness, harness.TenantId, survivor));
        }

        /// <summary>Contract: deleting an object that does not exist succeeds, beside ones that do.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Deleting_a_missing_object_succeeds()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string existing = NewName();
            await harness.Store.WriteAsync(harness.TenantId, [Write(existing, Bytes("x"))], Token);

            await harness.Store.DeleteAsync(harness.TenantId, [NewName(), existing, NewName("ff")], Token);

            Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, existing, Token));

            // Deleting the same name again is still success.
            await harness.Store.DeleteAsync(harness.TenantId, [existing], Token);
        }

        /// <summary>Contract: deleting in a tenant whose storage does not exist succeeds and creates nothing.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Deleting_in_a_tenant_without_storage_succeeds_and_creates_nothing()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.RemoveTenantStorageAsync(harness.TenantId);

            await harness.Store.DeleteAsync(harness.TenantId, [NewName(), NewName()], Token);

            Assert.False(await harness.TenantStorageExistsAsync(harness.TenantId));
        }

        /// <summary>Contract: a kind prefix lists the kind's objects, a fan-out prefix one segment's, in name order.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Lists_objects_under_a_kind_and_under_a_fan_out_segment_in_name_order()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string[] inAb = [NewName("ab"), NewName("ab")];
            string[] inCd = [NewName("cd")];
            string thumb = BlobName.Variant(inAb[0], ThumbnailPolicy.VariantName);
            byte[] payload = Bytes("payload");
            await harness.Store.WriteAsync(
                harness.TenantId, [.. inAb.Concat(inCd).Append(thumb).Select(name => Write(name, payload))], Token);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            IReadOnlyList<BlobStoreEntry> all = await ListAsync(harness, harness.TenantId, Kind);
            string[] expectedAll = [.. inAb.Concat(inCd).Append(thumb).Order(StringComparer.Ordinal)];
            Assert.Equal(expectedAll, all.Select(static entry => entry.Name));
            Assert.All(all, entry =>
            {
                Assert.Equal(payload.Length, entry.Length);
                Assert.Equal(TimeSpan.Zero, entry.LastModified.Offset);
                Assert.InRange(entry.LastModified, now.AddHours(-1), now.AddHours(1));
            });

            IReadOnlyList<BlobStoreEntry> segment = await ListAsync(harness, harness.TenantId, Kind + "/ab");
            string[] expectedSegment = [.. inAb.Append(thumb).Order(StringComparer.Ordinal)];
            Assert.Equal(expectedSegment, segment.Select(static entry => entry.Name));

            Assert.Empty(await ListAsync(harness, harness.TenantId, Kind + "/ef"));
            Assert.Empty(await ListAsync(harness, harness.TenantId, "conformance-none"));
        }

        /// <summary>Contract: a prefix addresses whole segments; a kind never lists a longer kind that starts with it.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task A_kind_prefix_never_lists_another_kind_that_starts_with_it()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string mine = NewName("ab");
            string other = NewName("ab", LongerKind);
            await harness.Store.WriteAsync(harness.TenantId, [Write(mine, Bytes("a")), Write(other, Bytes("b"))], Token);

            Assert.Equal(mine, Assert.Single(await ListAsync(harness, harness.TenantId, Kind)).Name);
            Assert.Equal(other, Assert.Single(await ListAsync(harness, harness.TenantId, LongerKind)).Name);
        }

        /// <summary>Contract: listing a tenant whose storage does not exist yields nothing and creates nothing.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Listing_a_tenant_without_storage_yields_nothing_and_creates_nothing()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.RemoveTenantStorageAsync(harness.TenantId);

            Assert.Empty(await ListAsync(harness, harness.TenantId, Kind));
            Assert.Empty(await ListAsync(harness, harness.TenantId, Kind + "/ab"));
            Assert.False(await harness.TenantStorageExistsAsync(harness.TenantId));
        }

        /// <summary>Contract: the purge removes incomplete artefacts older than the cut-off, keeps younger ones and every object.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Purges_incomplete_artefacts_older_than_the_cut_off_and_nothing_else()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            Assert.SkipUnless(
                harness.Capabilities.HasFlag(BlobStoreCapabilities.IncompleteArtefacts),
                "This store's writes are atomic on the service side, so it never leaves an incomplete artefact to purge.");

            string name = NewName();
            await harness.Store.WriteAsync(harness.TenantId, [Write(name, Bytes("kept"))], Token);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string old = await harness.PlantIncompleteArtefactAsync(harness.TenantId, now.AddHours(-25));
            string young = await harness.PlantIncompleteArtefactAsync(harness.TenantId, now.AddHours(-1));

            await harness.Store.PurgeIncompleteAsync(harness.TenantId, now.AddHours(-24), Token);

            Assert.False(await harness.IncompleteArtefactExistsAsync(harness.TenantId, old));
            Assert.True(await harness.IncompleteArtefactExistsAsync(harness.TenantId, young));
            Assert.Equal(Bytes("kept"), await ReadAsync(harness, harness.TenantId, name));
        }

        /// <summary>Contract: the purge is a no-op for a tenant without storage, and creates nothing.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Purging_a_tenant_without_storage_succeeds_and_creates_nothing()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.RemoveTenantStorageAsync(harness.TenantId);

            await harness.Store.PurgeIncompleteAsync(harness.TenantId, DateTimeOffset.UtcNow, Token);

            Assert.False(await harness.TenantStorageExistsAsync(harness.TenantId));
        }

        /// <summary>Contract: ensuring a tenant creates its storage, and ensuring it again changes nothing.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Ensuring_a_tenant_is_idempotent()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.RemoveTenantStorageAsync(harness.TenantId);

            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            Assert.True(await harness.TenantStorageExistsAsync(harness.TenantId));

            string name = NewName();
            await harness.Store.WriteAsync(harness.TenantId, [Write(name, Bytes("x"))], Token);
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);

            Assert.Equal(Bytes("x"), await ReadAsync(harness, harness.TenantId, name));
        }

        /// <summary>Contract: the first write for a tenant whose storage was never created creates it.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task The_first_write_for_a_tenant_without_storage_creates_it()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.RemoveTenantStorageAsync(harness.TenantId);
            string name = NewName();

            await harness.Store.WriteAsync(harness.TenantId, [Write(name, Bytes("lazy"))], Token);

            Assert.True(await harness.TenantStorageExistsAsync(harness.TenantId));
            Assert.Equal(Bytes("lazy"), await ReadAsync(harness, harness.TenantId, name));
        }

        /// <summary>Contract: a write into a tenant whose storage was removed behind the store's back recreates it and succeeds.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task A_write_into_a_tenant_whose_storage_was_removed_recreates_it_and_succeeds()
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            await harness.Store.WriteAsync(harness.TenantId, [Write(NewName(), Bytes("before"))], Token);
            await harness.RemoveTenantStorageAsync(harness.TenantId);
            string primary = NewName();
            string thumb = BlobName.Variant(primary, ThumbnailPolicy.VariantName);

            await harness.Store.WriteAsync(harness.TenantId, [Write(primary, Bytes("after")), Write(thumb, Bytes("thumb"))], Token);

            Assert.Equal(Bytes("after"), await ReadAsync(harness, harness.TenantId, primary));
            Assert.Equal(Bytes("thumb"), await ReadAsync(harness, harness.TenantId, thumb));
        }

        /// <summary>Contract: every member refuses a name outside the grammar, traversal included, before touching storage.</summary>
        /// <param name="name">The invalid name.</param>
        /// <returns>A task that completes when the case has run.</returns>
        [Theory]
        [MemberData(nameof(InvalidNames), MemberType = typeof(BlobStoreConformanceTests))]
        public async Task Names_outside_the_grammar_are_refused_before_storage_is_touched(string name)
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            await harness.RemoveTenantStorageAsync(harness.TenantId);
            string valid = NewName();

            // The valid write beside the invalid one must not happen either.
            await Assert.ThrowsAnyAsync<ArgumentException>(
                () => harness.Store.WriteAsync(harness.TenantId, [Write(valid, Bytes("valid")), Write(name, Bytes("x"))], Token));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => harness.Store.OpenReadAsync(harness.TenantId, name, Token));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => harness.Store.DeleteAsync(harness.TenantId, [name], Token));

            Assert.False(await harness.TenantStorageExistsAsync(harness.TenantId));
        }

        /// <summary>Contract: a listing prefix outside the grammar is refused.</summary>
        /// <param name="prefix">The invalid prefix.</param>
        /// <returns>A task that completes when the case has run.</returns>
        [Theory]
        [InlineData("")]
        [InlineData("..")]
        [InlineData("../t2")]
        [InlineData(Kind + "/")]
        [InlineData(Kind + "/a")]
        [InlineData(Kind + "/AB")]
        [InlineData(Kind + "/ab/")]
        [InlineData(Kind + "/../..")]
        [InlineData("/" + Kind)]
        [InlineData("Conformance")]
        public async Task A_listing_prefix_outside_the_grammar_is_refused(string prefix)
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();

            await Assert.ThrowsAnyAsync<ArgumentException>(() => ListAsync(harness, harness.TenantId, prefix));
        }

        /// <summary>Contract: a tenant id that is not positive is refused by every member.</summary>
        /// <param name="tenantId">The invalid tenant id.</param>
        /// <returns>A task that completes when the case has run.</returns>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public async Task A_tenant_id_that_is_not_positive_is_refused(int tenantId)
        {
            await using IBlobStoreHarness harness = await CreateHarnessAsync();
            string name = NewName();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Store.EnsureTenantAsync(tenantId, Token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Store.WriteAsync(tenantId, [Write(name, Bytes("x"))], Token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Store.OpenReadAsync(tenantId, name, Token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Store.DeleteAsync(tenantId, [name], Token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ListAsync(harness, tenantId, Kind));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.Store.PurgeIncompleteAsync(tenantId, DateTimeOffset.UtcNow, Token));
        }

        /// <summary>The running case's cancellation token.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>A random key whose first two digits are <paramref name="fanOut" />.</summary>
        /// <param name="fanOut">Two lowercase hexadecimal digits.</param>
        /// <returns>The key.</returns>
        private static string KeyWithFanOut(string fanOut)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{fanOut}{Guid.NewGuid().ToString("N")[BlobName.FanOutLength..]}");
        }

        /// <summary>Reads an object that must exist.</summary>
        /// <param name="harness">The harness.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="name">The object's name.</param>
        /// <returns>Its bytes.</returns>
        private static async Task<byte[]> ReadAsync(IBlobStoreHarness harness, int tenantId, string name)
        {
            await using BlobContent? content = await harness.Store.OpenReadAsync(tenantId, name, Token);
            Assert.NotNull(content);
            byte[] bytes = await ReadToEndAsync(content.Content);
            Assert.Equal(content.Length, bytes.Length);
            return bytes;
        }

        /// <summary>Reads a stream to its end.</summary>
        /// <param name="stream">The stream.</param>
        /// <returns>Its remaining bytes.</returns>
        private static async Task<byte[]> ReadToEndAsync(Stream stream)
        {
            using MemoryStream buffer = new();
            await stream.CopyToAsync(buffer, Token);
            return buffer.ToArray();
        }

        /// <summary>Lists a prefix to the end.</summary>
        /// <param name="harness">The harness.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="prefix">The prefix.</param>
        /// <returns>The entries.</returns>
        private static async Task<IReadOnlyList<BlobStoreEntry>> ListAsync(IBlobStoreHarness harness, int tenantId, string prefix)
        {
            List<BlobStoreEntry> entries = [];
            await foreach (BlobStoreEntry entry in harness.Store.ListAsync(tenantId, prefix, Token))
            {
                entries.Add(entry);
            }

            return entries;
        }
    }
}
