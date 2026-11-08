// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Blobs;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     What the file-system store does beyond the shared contract: its layout on disk, its
    ///     temporary files, its root rules, its log events and its instruments.
    /// </summary>
    public class FileSystemBlobStoreTests
    {
        // The meter factory of stores whose instruments a case does not observe.
        private static readonly ServiceProvider Metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Objects_land_under_the_tenant_kind_and_fan_out_directories()
        {
            await using FileSystemBlobStoreHarness harness = new();
            string primary = BlobStoreConformanceTests.NewName("3f");
            string thumb = BlobName.Variant(primary, ThumbnailPolicy.VariantName);

            await harness.Store.WriteAsync(
                7, [BlobStoreConformanceTests.Write(primary, [1]), BlobStoreConformanceTests.Write(thumb, [2])], Token);

            string key = primary.Split('/')[2];
            string directory = Path.Combine(harness.RootPath, "t7", BlobStoreConformanceTests.Kind, "3f");
            Assert.Equal([1], await File.ReadAllBytesAsync(Path.Combine(directory, key), Token));
            Assert.Equal([2], await File.ReadAllBytesAsync(Path.Combine(directory, key + ".thumb"), Token));
            Assert.True(Directory.Exists(Path.Combine(harness.RootPath, "t7", FileSystemBlobStore.TemporaryDirectoryName)));
        }

        [Fact]
        public async Task Neither_a_successful_nor_a_conflicting_write_leaves_a_part_file()
        {
            await using FileSystemBlobStoreHarness harness = new();
            string name = BlobStoreConformanceTests.NewName();
            await harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [1, 2, 3])], Token);

            await Assert.ThrowsAsync<BlobAlreadyExistsException>(
                () => harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [4])], Token));

            Assert.Empty(Directory.GetFiles(harness.TemporaryDirectory(harness.TenantId)));
        }

        [Fact]
        public async Task A_cancelled_write_leaves_neither_an_object_nor_a_part_file()
        {
            await using FileSystemBlobStoreHarness harness = new();
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            string name = BlobStoreConformanceTests.NewName();
            using CancellationTokenSource cancellation = new();
            await using CancellingStream content = new(cancellation);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => harness.Store.WriteAsync(harness.TenantId, [new BlobWrite(name, "text/plain", content, 1_000_000)], cancellation.Token));

            Assert.False(File.Exists(harness.ObjectPath(harness.TenantId, name)));
            Assert.Empty(Directory.GetFiles(harness.TemporaryDirectory(harness.TenantId)));
        }

        [Fact]
        public async Task A_writer_that_loses_the_race_after_the_early_check_is_refused_by_the_publish()
        {
            await using FileSystemBlobStoreHarness harness = new();
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            string name = BlobStoreConformanceTests.NewName();
            await using GatedStream slow = new([1, 1, 1]);

            // The slow writer passes the early existence check, then blocks inside its copy while
            // a second writer of the same name publishes; only the publish can refuse it now.
            Task slowWrite = harness.Store.WriteAsync(harness.TenantId, [new BlobWrite(name, "text/plain", slow, 3)], Token);
            await slow.Reading.WaitAsync(Token);
            await harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [2, 2, 2])], Token);
            slow.Release();

            BlobAlreadyExistsException conflict = await Assert.ThrowsAsync<BlobAlreadyExistsException>(() => slowWrite);
            Assert.Equal(name, conflict.Name);
            Assert.Equal([2, 2, 2], await File.ReadAllBytesAsync(harness.ObjectPath(harness.TenantId, name), Token));
            Assert.Empty(Directory.GetFiles(harness.TemporaryDirectory(harness.TenantId)));
        }

        [Fact]
        public async Task A_write_whose_lazy_ensure_fails_throws_once_and_leaves_nothing()
        {
            FakeLogger<FileSystemBlobStore> logger = new();
            await using FileSystemBlobStoreHarness harness = new(logger);

            // A regular file where the tenant's .tmp directory belongs: the part file cannot be
            // created, and neither can the directory the retry would create.
            Directory.CreateDirectory(harness.TenantDirectory(harness.TenantId));
            await File.WriteAllTextAsync(harness.TemporaryDirectory(harness.TenantId), "not a directory", Token);
            string name = BlobStoreConformanceTests.NewName();

            await Assert.ThrowsAsync<BlobStoreException>(
                () => harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [1])], Token));

            Assert.Single(logger.Collector.GetSnapshot(), static record => record.Id.Name == "BlobTenantMissingOnWrite");
            Assert.False(File.Exists(harness.ObjectPath(harness.TenantId, name)));
            Assert.True(File.Exists(harness.TemporaryDirectory(harness.TenantId)));
        }

        [Fact]
        public async Task The_create_only_probe_passes_on_the_test_file_system_and_leaves_nothing()
        {
            await using FileSystemBlobStoreHarness harness = new();

            Assert.Null(FileSystemBlobStore.ProbeCreateOnlyPublish(harness.RootPath));

            Assert.Empty(Directory.Exists(harness.RootPath) ? Directory.GetFileSystemEntries(harness.RootPath) : []);
        }

        [Fact]
        public async Task A_read_stream_lets_the_object_be_deleted_and_read_concurrently()
        {
            await using FileSystemBlobStoreHarness harness = new();
            string name = BlobStoreConformanceTests.NewName();
            byte[] bytes = [.. Enumerable.Range(0, 4096).Select(static i => (byte)i)];
            await harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, bytes)], Token);

            await using BlobContent? first = await harness.Store.OpenReadAsync(harness.TenantId, name, Token);
            await using BlobContent? second = await harness.Store.OpenReadAsync(harness.TenantId, name, Token);
            Assert.NotNull(first);
            Assert.NotNull(second);

            // The sweep may delete an object while a download of it is still streaming.
            await harness.Store.DeleteAsync(harness.TenantId, [name], Token);

            using MemoryStream copy = new();
            await first.Content.CopyToAsync(copy, Token);
            Assert.Equal(bytes, copy.ToArray());
        }

        [Fact]
        public async Task A_listing_skips_files_and_directories_that_are_not_blob_names()
        {
            await using FileSystemBlobStoreHarness harness = new();
            string name = BlobStoreConformanceTests.NewName("ab");
            await harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [1])], Token);
            string kindDirectory = Path.Combine(harness.TenantDirectory(harness.TenantId), BlobStoreConformanceTests.Kind);

            // Strays: a readme in a fan-out directory, an upper-case key, a key in the wrong
            // fan-out directory, and directories that are not fan-out segments.
            await File.WriteAllTextAsync(Path.Combine(kindDirectory, "ab", "README.txt"), "x", Token);
            await File.WriteAllTextAsync(Path.Combine(kindDirectory, "ab", "AB" + new string('C', 30)), "x", Token);
            await File.WriteAllTextAsync(Path.Combine(kindDirectory, "ab", "cd" + new string('0', 30)), "x", Token);
            Directory.CreateDirectory(Path.Combine(kindDirectory, "zz"));
            await File.WriteAllTextAsync(Path.Combine(kindDirectory, "zz", "zz" + new string('0', 30)), "x", Token);
            Directory.CreateDirectory(Path.Combine(kindDirectory, "abc"));
            await File.WriteAllTextAsync(Path.Combine(kindDirectory, "loose-file"), "x", Token);

            List<string> listed = [];
            await foreach (BlobStoreEntry entry in harness.Store.ListAsync(harness.TenantId, BlobStoreConformanceTests.Kind, Token))
            {
                listed.Add(entry.Name);
            }

            Assert.Equal([name], listed);
        }

        [Fact]
        public async Task The_purge_ignores_files_in_the_temporary_directory_that_are_not_part_files()
        {
            await using FileSystemBlobStoreHarness harness = new();
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            string notes = Path.Combine(harness.TemporaryDirectory(harness.TenantId), "notes.txt");
            await File.WriteAllTextAsync(notes, "x", Token);
            File.SetLastWriteTimeUtc(notes, DateTime.UtcNow.AddDays(-30));

            await harness.Store.PurgeIncompleteAsync(harness.TenantId, DateTimeOffset.UtcNow, Token);

            Assert.True(File.Exists(notes));
        }

        [Theory]
        [InlineData(null, "is not set")]
        [InlineData("", "is not set")]
        [InlineData("   ", "is not set")]
        [InlineData("blobs", "is not absolute")]
        [InlineData("./blobs", "is not absolute")]
        [InlineData("../blobs", "is not absolute")]
        [InlineData("C:blobs", "is not absolute")]
        public void A_root_that_is_unset_or_relative_is_refused(string? rootPath, string expected)
        {
            string failure = Assert.Single(FileSystemBlobStoreOptionsValidator.Describe(rootPath, isWindows: false));
            Assert.Contains(expected, failure, StringComparison.Ordinal);
            Assert.Contains("Tellma:Blobs:FileSystem:RootPath", failure, StringComparison.Ordinal);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Create(rootPath));
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_rooted_but_drive_relative_path_is_refused_on_windows()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has rooted paths that are not fully qualified.");

            Assert.Contains("is not absolute", Assert.Single(FileSystemBlobStoreOptionsValidator.Describe("\\blobs", isWindows: true)), StringComparison.Ordinal);
        }

        [Fact]
        public void On_windows_the_root_is_at_most_153_characters()
        {
            string root = Path.Combine(Path.GetTempPath(), "r");
            string longest = root + new string('x', FileSystemBlobStoreOptions.MaxWindowsRootPathLength - root.Length);
            string tooLong = longest + "x";

            Assert.Equal(153, FileSystemBlobStoreOptions.MaxWindowsRootPathLength);
            Assert.Empty(FileSystemBlobStoreOptionsValidator.Describe(longest, isWindows: true));
            Assert.Contains("at most 153", Assert.Single(FileSystemBlobStoreOptionsValidator.Describe(tooLong, isWindows: true)), StringComparison.Ordinal);
            Assert.Empty(FileSystemBlobStoreOptionsValidator.Describe(tooLong, isWindows: false));
        }

        [Fact]
        public void The_longest_root_keeps_the_deepest_object_path_within_259_characters()
        {
            // The deepest object: the largest tenant id, a maximal kind, fan-out, key and variant.
            string root = new('r', FileSystemBlobStoreOptions.MaxWindowsRootPathLength);
            string name = BlobName.Variant(
                BlobName.Primary(new string('k', BlobName.MaxKindLength), new string('0', BlobName.KeyLength)),
                new string('v', BlobName.MaxVariantLength));
            string deepest = root + "\\t" + int.MaxValue + "\\" + name.Replace('/', '\\');

            Assert.Equal(259, deepest.Length);
        }

        [Fact]
        public async Task The_root_is_normalised_and_logged_once_when_the_store_is_created()
        {
            string root = Path.Combine(Path.GetTempPath(), "tellma-blobs-" + Guid.NewGuid().ToString("N"));
            string spelled = Path.Combine(root, "sub", "..") + Path.DirectorySeparatorChar;
            FakeLogger<FileSystemBlobStore> logger = new();

            FileSystemBlobStore store = Create(spelled, logger);

            Assert.Equal(root, store.RootPath);
            FakeLogRecord record = Assert.Single(logger.Collector.GetSnapshot());
            Assert.Equal("BlobFileSystemRoot", record.Id.Name);
            Assert.Contains(root, record.Message, StringComparison.Ordinal);

            // The store creates nothing until it writes.
            Assert.False(Directory.Exists(root));
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Ensuring_and_a_write_into_missing_storage_are_logged()
        {
            FakeLogger<FileSystemBlobStore> logger = new();
            await using FileSystemBlobStoreHarness harness = new(logger);

            await harness.Store.EnsureTenantAsync(3, Token);
            await harness.RemoveTenantStorageAsync(3);
            await harness.Store.WriteAsync(3, [BlobStoreConformanceTests.Write(BlobStoreConformanceTests.NewName(), [1])], Token);

            string[] events = [.. logger.Collector.GetSnapshot().Select(static record => record.Id.Name ?? string.Empty)];
            Assert.Equal(["BlobFileSystemRoot", "BlobTenantEnsured", "BlobTenantMissingOnWrite", "BlobTenantEnsured"], events);
        }

        [Fact]
        public async Task Every_store_call_is_metered_with_its_operation_and_outcome_and_no_tenant()
        {
            await using FileSystemBlobStoreHarness harness = new();
            using MetricCollector<long> operations = new(harness.MeterFactory, BlobTelemetryNames.StoreMeterName, BlobTelemetryNames.StoreOperations);
            using MetricCollector<double> durations = new(harness.MeterFactory, BlobTelemetryNames.StoreMeterName, BlobTelemetryNames.StoreDuration);
            string name = BlobStoreConformanceTests.NewName();

            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            await harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [1, 2])], Token);
            await Assert.ThrowsAsync<BlobAlreadyExistsException>(
                () => harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [3])], Token));
            await Assert.ThrowsAsync<BlobStoreException>(() => harness.Store.WriteAsync(
                harness.TenantId, [new BlobWrite(BlobStoreConformanceTests.NewName(), "text/plain", new MemoryStream([1]), 2)], Token));
            (await harness.Store.OpenReadAsync(harness.TenantId, name, Token))!.Dispose();
            Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, BlobStoreConformanceTests.NewName(), Token));
            await harness.Store.DeleteAsync(harness.TenantId, [name], Token);
            await foreach (BlobStoreEntry _ in harness.Store.ListAsync(harness.TenantId, BlobStoreConformanceTests.Kind, Token))
            {
            }

            await harness.Store.PurgeIncompleteAsync(harness.TenantId, DateTimeOffset.UtcNow, Token);

            (string Operation, string Outcome)[] expected =
            [
                ("ensure", "ok"),
                ("write", "ok"),
                ("write", "conflict"),
                ("write", "failed"),
                ("read", "ok"),
                ("read", "not_found"),
                ("delete", "ok"),
                ("list", "ok"),
                ("purge", "ok"),
            ];
            IReadOnlyList<CollectedMeasurement<long>> measured = operations.GetMeasurementSnapshot();
            Assert.Equal(expected, measured.Select(static m => ((string)m.Tags["operation"]!, (string)m.Tags["outcome"]!)));
            Assert.All(measured, static m =>
            {
                Assert.Equal(1, m.Value);
                Assert.Equal(["operation", "outcome"], m.Tags.Keys.Order(StringComparer.Ordinal));
            });

            // Durations carry the operation, and a write's carries its size class as well.
            IReadOnlyList<CollectedMeasurement<double>> timed = durations.GetMeasurementSnapshot();
            Assert.Equal(expected.Select(static e => e.Operation), timed.Select(static m => (string)m.Tags["operation"]!));
            Assert.All(timed, static m =>
            {
                Assert.True(m.Value >= 0);
                string[] keys = [.. m.Tags.Keys.Order(StringComparer.Ordinal)];
                Assert.Equal((string)m.Tags["operation"]! == "write" ? ["operation", "size_class"] : ["operation"], keys);
            });
            Assert.All(timed.Where(static m => (string)m.Tags["operation"]! == "write"), static m => Assert.Equal("under_1mib", m.Tags["size_class"]));
        }

        [Fact]
        public async Task A_write_is_size_classed_by_its_total_bytes()
        {
            await using FileSystemBlobStoreHarness harness = new();
            using MetricCollector<double> durations = new(harness.MeterFactory, BlobTelemetryNames.StoreMeterName, BlobTelemetryNames.StoreDuration);
            string primary = BlobStoreConformanceTests.NewName();

            // Two objects of 600 KiB: neither is 1 MiB, together they are.
            byte[] bytes = new byte[600 * 1024];
            await harness.Store.WriteAsync(
                harness.TenantId,
                [BlobStoreConformanceTests.Write(primary, bytes), BlobStoreConformanceTests.Write(BlobName.Variant(primary, "thumb"), bytes)],
                Token);

            Assert.Equal("1_to_10mib", Assert.Single(durations.GetMeasurementSnapshot()).Tags["size_class"]);
        }

        [Fact]
        public async Task Calls_refused_for_their_arguments_are_not_metered()
        {
            await using FileSystemBlobStoreHarness harness = new();
            using MetricCollector<long> operations = new(harness.MeterFactory, BlobTelemetryNames.StoreMeterName, BlobTelemetryNames.StoreOperations);

            await Assert.ThrowsAnyAsync<ArgumentException>(() => harness.Store.OpenReadAsync(harness.TenantId, "../x", Token));
            await Assert.ThrowsAnyAsync<ArgumentException>(
                () => harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write("../x", [1])], Token));

            Assert.Empty(operations.GetMeasurementSnapshot());
        }

        [Theory]
        [InlineData(0, "under_1mib")]
        [InlineData((1024 * 1024) - 1, "under_1mib")]
        [InlineData(1024 * 1024, "1_to_10mib")]
        [InlineData(10 * 1024 * 1024, "1_to_10mib")]
        [InlineData((10 * 1024 * 1024) + 1, "over_10mib")]
        [InlineData(long.MaxValue, "over_10mib")]
        public void Sizes_fall_into_three_classes(long bytes, string expected)
        {
            Assert.Equal(expected, BlobTelemetryTagValues.SizeClass(bytes));
        }

        /// <summary>Creates a store over <paramref name="rootPath" />.</summary>
        /// <param name="rootPath">The root.</param>
        /// <param name="logger">The logger; a null logger when omitted.</param>
        /// <returns>The store.</returns>
        private static FileSystemBlobStore Create(string? rootPath, FakeLogger<FileSystemBlobStore>? logger = null)
        {
            return new FileSystemBlobStore(
                Options.Create(new FileSystemBlobStoreOptions { RootPath = rootPath }),
                new BlobStoreMetrics(Metrics.GetRequiredService<IMeterFactory>()),
                (ILogger<FileSystemBlobStore>?)logger ?? NullLogger<FileSystemBlobStore>.Instance);
        }

        /// <summary>A stream whose first read waits for <see cref="Release" />, to hold a write inside its copy.</summary>
        /// <param name="bytes">The content.</param>
        private sealed class GatedStream(byte[] bytes) : MemoryStream(bytes)
        {
            private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _reads;

            /// <summary>Signalled when the first read starts.</summary>
            public SemaphoreSlim Reading { get; } = new(0, 1);

            /// <summary>Lets the first read complete.</summary>
            public void Release()
            {
                _released.TrySetResult();
            }

            /// <inheritdoc />
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref _reads) == 1)
                {
                    Reading.Release();
                    await _released.Task.WaitAsync(cancellationToken);
                }

                return await base.ReadAsync(buffer, cancellationToken);
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Reading.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        /// <summary>A stream that cancels its own write after the first read, to interrupt a write mid-copy.</summary>
        /// <param name="cancellation">The source to cancel.</param>
        private sealed class CancellingStream(CancellationTokenSource cancellation) : MemoryStream(new byte[1_000_000])
        {
            /// <inheritdoc />
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                int read = await base.ReadAsync(buffer, cancellationToken);
                await cancellation.CancelAsync();
                return read;
            }
        }
    }
}
