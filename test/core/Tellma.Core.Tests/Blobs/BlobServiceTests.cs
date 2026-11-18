// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Blobs;
using Tellma.Core.Calendars;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Testing.Blobs;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     Staging over an in-memory store and a batch no database answers: what is stored and
    ///     recorded for an attachment, a streamed attachment and an image; each refusal, counted and
    ///     leaving nothing behind; the quota's translation and the platform scope's exemption; and the
    ///     row written before the bytes when the store fails.
    /// </summary>
    public sealed class BlobServiceTests : IDisposable
    {
        /// <summary>The instant the scope runs at.</summary>
        private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The deadline the database answers with.</summary>
        private static readonly DateTimeOffset Expires = Now.AddHours(24);

        /// <summary>An attachment kind with a thumbnail.</summary>
        private static readonly BlobKindDescriptor File = Descriptor("bm-file", BlobKindPolicy.Attachment);

        /// <summary>An image kind.</summary>
        private static readonly BlobKindDescriptor Photo = Descriptor("bm-photo", BlobKindPolicy.Photo);

        /// <summary>An attachment kind of at most 100 bytes.</summary>
        private static readonly BlobKindDescriptor Small = Descriptor("bm-small", BlobKindPolicy.Attachment with { MaxSize = 100, Thumbnail = null });

        /// <summary>The batches, over the default composition with a connection that refuses.</summary>
        private readonly BatchHarness _harness = new();

        /// <summary>The store the service writes to.</summary>
        private readonly InMemoryBlobStore _store = new();

        /// <summary>The processor the service normalises and renders with.</summary>
        private readonly StubImageProcessor _processor = new();

        /// <summary>The service's logs.</summary>
        private readonly FakeLogger<BlobService> _logger = new();

        /// <summary>The meters the instruments are created on.</summary>
        private readonly ServiceProvider _meters = new ServiceCollection().AddMetrics().BuildServiceProvider();

        /// <summary>The scratch directory, removed with the suite's instance.</summary>
        private readonly string _scratch = Path.Join(Path.GetTempPath(), "tellma-blob-scratch-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        /// <summary>The scope's context.</summary>
        private readonly Holder _context = new();

        /// <summary>The runner, answering for a database.</summary>
        private readonly StageRunner _runner;

        /// <summary>Every upload counted, collected from before the first service is created.</summary>
        private readonly MetricCollector<long> _uploads;

        /// <summary>Creates the runner over the batches and the upload collector.</summary>
        public BlobServiceTests()
        {
            _runner = new StageRunner(_harness);
            _uploads = Collector<long>(BlobTelemetryNames.Uploads);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _uploads.Dispose();
            _meters.Dispose();
            _harness.Dispose();
            if (Directory.Exists(_scratch))
            {
                Directory.Delete(_scratch, recursive: true);
            }
        }

        [Fact]
        public async Task An_attachment_is_recorded_then_written_with_its_thumbnail()
        {
            using MetricCollector<long> bytes = Collector<long>(BlobTelemetryNames.UploadBytes);
            using MetricCollector<double> duration = Collector<double>(BlobTelemetryNames.UploadDuration);
            byte[] pdf = Pdf(1000);

            BlobDescriptor staged = await Service().StageAsync(Request(File.Kind, pdf, BlobContentTypes.Pdf, "  Q3/report.pdf "), Token);

            // --- The descriptor ---
            Assert.Equal(new BlobDescriptor(7, File.Kind, BlobContentTypes.Pdf, 1000, null, null, "Q3_report.pdf", Expires), staged);

            // --- The row: the uploader, the stored bytes, the variant, the lifetime and the quota ---
            Assert.Equal(1, _runner.Runs);
            Assert.Equal(BatchPurpose.Persist, _runner.Purpose);
            Dictionary<string, object?> row = _runner.Parameters;
            string storageKey = (string)row["p2"]!;
            Assert.True(BlobName.IsValidPrimary(storageKey));
            Assert.StartsWith(File.Kind + "/", storageKey, StringComparison.Ordinal);
            Assert.Equal(
                (42, File.Kind, BlobContentTypes.Pdf, "Q3_report.pdf", 1000L, "thumb", 86400, 200, 512L * 1024 * 1024),
                ((int)row["p0"]!, (string)row["p1"]!, (string)row["p3"]!, (string)row["p4"]!, (long)row["p5"]!, (string)row["p9"]!, (int)row["p10"]!, (int)row["p11"]!, (long)row["p12"]!));
            Assert.Equal(SHA256.HashData(pdf), (byte[])row["p6"]!);
            Assert.Null(row["p7"]);

            // --- The objects: the upload itself and its thumbnail ---
            Assert.Equal([storageKey, BlobName.Variant(storageKey, ThumbnailPolicy.VariantName)], _store.Names(1));
            Assert.Equal((pdf, BlobContentTypes.Pdf), _store.Find(1, storageKey));
            Assert.Equal(BlobContentTypes.WebP, _store.Find(1, BlobName.Variant(storageKey, ThumbnailPolicy.VariantName))!.Value.ContentType);
            Assert.Equal(1, _processor.ThumbnailCalls);

            // --- The instruments and the log ---
            Assert.Equal(
                [(1L, File.Kind, BlobTelemetryTagValues.UploadStaged)],
                _uploads.GetMeasurementSnapshot().Select(static m => (m.Value, (string)m.Tags[BlobTelemetryNames.KindTag]!, (string)m.Tags[BlobTelemetryNames.OutcomeTag]!)));
            Assert.Equal(1000L, Assert.Single(bytes.GetMeasurementSnapshot()).Value);
            Assert.Equal(BlobTelemetryTagValues.SizeUnder1MiB, Assert.Single(duration.GetMeasurementSnapshot()).Tags[BlobTelemetryNames.SizeClassTag]);
            Assert.Equal("BlobStaged", _logger.LatestRecord.Id.Name);
        }

        [Fact]
        public async Task A_large_attachment_streams_through_a_scratch_file_that_is_gone_afterwards()
        {
            byte[] pdf = Pdf(200 * 1024);

            BlobDescriptor staged = await Service().StageAsync(Request(File.Kind, pdf, BlobContentTypes.Pdf), Token);

            string storageKey = (string)_runner.Parameters["p2"]!;
            Assert.Equal(pdf, _store.Find(1, storageKey)!.Value.Bytes);
            Assert.Equal(SHA256.HashData(pdf), (byte[])_runner.Parameters["p6"]!);
            Assert.Equal(pdf.LongLength, staged.Size);
            Assert.True(Directory.Exists(_scratch));
            Assert.Empty(Directory.EnumerateFileSystemEntries(_scratch));
        }

        [Fact]
        public async Task An_image_stores_and_records_the_processors_output_never_the_clients_bytes()
        {
            RecodingProcessor recoding = new();

            BlobDescriptor staged = await Service(recoding).StageAsync(Request(Photo.Kind, Png(5000), "image/png"), Token);

            Assert.Equal((BlobContentTypes.WebP, 3L, 1600, 900), (staged.ContentType, staged.Size, staged.Width, staged.Height));
            Dictionary<string, object?> row = _runner.Parameters;
            Assert.Equal(SHA256.HashData(RecodingProcessor.Output), (byte[])row["p6"]!);
            Assert.Equal((3L, 1600, 900, BlobContentTypes.WebP, "thumb"), ((long)row["p5"]!, (int)row["p7"]!, (int)row["p8"]!, (string)row["p3"]!, (string)row["p9"]!));
            string storageKey = (string)row["p2"]!;
            Assert.Equal(RecodingProcessor.Output, _store.Find(1, storageKey)!.Value.Bytes);
            Assert.Equal(RecodingProcessor.Thumbnail, _store.Find(1, BlobName.Variant(storageKey, ThumbnailPolicy.VariantName))!.Value.Bytes);
            Assert.Equal((Photo.Kind, BlobKindPolicy.Photo.Image, TimeSpan.FromSeconds(15)), (recoding.Kind, recoding.Policy, recoding.Timeout));
        }

        [Fact]
        public async Task A_file_the_processor_cannot_render_has_no_thumbnail_and_no_variant()
        {
            _processor.RenderThumbnails = false;

            _ = await Service().StageAsync(Request(File.Kind, Pdf(100), BlobContentTypes.Pdf), Token);

            Assert.Equal(string.Empty, _runner.Parameters["p9"]);
            Assert.Single(_store.Names(1));
        }

        [Fact]
        public async Task An_attachment_without_a_processor_is_staged_without_a_thumbnail()
        {
            _ = await Service(processor: null).StageAsync(Request(File.Kind, Pdf(100), BlobContentTypes.Pdf), Token);

            Assert.Equal(string.Empty, _runner.Parameters["p9"]);
            Assert.Single(_store.Names(1));
        }

        [Fact]
        public async Task An_unknown_kind_is_refused_and_never_measured()
        {

            BlobRejectedException refused = await Assert.ThrowsAsync<BlobRejectedException>(
                () => Service().StageAsync(Request("../Etc", Pdf(10), BlobContentTypes.Pdf), Token));

            Assert.Equal(BlobRejectedException.UnknownKind, refused.Code);
            Assert.Empty(_uploads.GetMeasurementSnapshot());
            Assert.Equal("An upload of kind - was refused: Blob.UnknownKind.", _logger.LatestRecord.Message);
            AssertNothingStaged();
        }

        [Fact]
        public async Task An_unknown_length_is_refused()
        {
            BlobRejectedException refused = await RefusedAsync(new BlobStageRequest(File.Kind, new MemoryStream(Pdf(10)), -1, BlobContentTypes.Pdf, null));

            Assert.Equal(BlobRejectedException.LengthRequired, refused.Code);
            AssertCounted(File.Kind, BlobTelemetryTagValues.UploadRejectedSize);
        }

        [Fact]
        public async Task A_declared_length_above_the_lower_of_the_two_limits_is_refused_with_that_limit()
        {
            BlobRejectedException refused = await RefusedAsync(
                Request(Small.Kind, Pdf(80), BlobContentTypes.Pdf), new BlobOptions { MaxUploadSize = 50 });

            Assert.Equal((BlobRejectedException.TooLarge, (object?)50L), (refused.Code, refused.Arguments[BlobRejectedException.MaxBytesArgument]));
            AssertCounted(Small.Kind, BlobTelemetryTagValues.UploadRejectedSize);
        }

        [Theory]
        [InlineData(1000)]
        [InlineData(200 * 1024)]
        public async Task A_body_longer_than_declared_is_too_large(int declared)
        {
            BlobRejectedException refused = await RefusedAsync(
                new BlobStageRequest(File.Kind, new MemoryStream(Pdf(declared + 1)), declared, BlobContentTypes.Pdf, null));

            Assert.Equal(BlobRejectedException.TooLarge, refused.Code);
            AssertCounted(File.Kind, BlobTelemetryTagValues.UploadRejectedSize);
        }

        [Theory]
        [InlineData(1000)]
        [InlineData(200 * 1024)]
        public async Task A_body_shorter_than_declared_is_refused(int declared)
        {
            BlobRejectedException refused = await RefusedAsync(
                new BlobStageRequest(File.Kind, new MemoryStream(Pdf(declared - 1)), declared, BlobContentTypes.Pdf, null));

            Assert.Equal(BlobRejectedException.LengthRequired, refused.Code);
            Assert.Empty(Directory.Exists(_scratch) ? Directory.EnumerateFileSystemEntries(_scratch) : []);
        }

        [Fact]
        public async Task A_type_outside_the_allowed_set_is_refused_naming_it()
        {
            byte[] executable = [(byte)'M', (byte)'Z', 0x90, 0x00, 0x03];

            BlobRejectedException refused = await RefusedAsync(Request(File.Kind, executable, "application/pdf"));

            Assert.Equal((BlobRejectedException.UnsupportedType, (object?)BlobContentTypes.Pdf), (refused.Code, refused.Arguments[BlobRejectedException.ContentTypeArgument]));
            AssertCounted(File.Kind, BlobTelemetryTagValues.UploadRejectedType);
        }

        [Fact]
        public async Task An_image_the_processor_refuses_is_refused_with_its_reason()
        {
            _processor.RejectReason = ImageRejectedException.TimeoutReason;

            BlobRejectedException refused = await RefusedAsync(Request(Photo.Kind, Png(100), "image/png"));

            Assert.Equal((BlobRejectedException.ImageRejected, (object?)ImageRejectedException.TimeoutReason), (refused.Code, refused.Arguments[BlobRejectedException.ReasonArgument]));
            AssertCounted(Photo.Kind, BlobTelemetryTagValues.UploadRejectedImage);
        }

        [Fact]
        public async Task A_busy_processor_passes_through_counted_apart_from_refusals()
        {
            _processor.Busy = true;

            _ = await Assert.ThrowsAsync<ImageProcessingBusyException>(() => Service().StageAsync(Request(Photo.Kind, Png(100), "image/png"), Token));

            AssertCounted(Photo.Kind, BlobTelemetryTagValues.UploadBusy);
            AssertNothingStaged();
        }

        [Fact]
        public async Task An_image_kind_without_a_processor_fails_loudly()
        {
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Service(processor: null).StageAsync(Request(Photo.Kind, Png(100), "image/png"), Token));

            Assert.Contains("no image processor is registered", failure.Message, StringComparison.Ordinal);
            AssertNothingStaged();
        }

        [Fact]
        public async Task The_quotas_assertion_is_a_refusal_with_both_limits()
        {
            _runner.Failure = new BatchAssertionFailedException(TellmaSqlErrors.Invariant, BlobRejectedException.StagingQuotaExceeded, 0);

            BlobRejectedException refused = await RefusedAsync(
                Request(File.Kind, Pdf(100), BlobContentTypes.Pdf), new BlobOptions { MaxStagedPerUser = 3, MaxStagedBytesPerUser = 4096 });

            Assert.Equal(BlobRejectedException.StagingQuotaExceeded, refused.Code);
            Assert.Equal((object?)3, refused.Arguments[BlobRejectedException.MaxCountArgument]);
            Assert.Equal((object?)4096L, refused.Arguments[BlobRejectedException.MaxBytesArgument]);
            AssertCounted(File.Kind, BlobTelemetryTagValues.UploadQuota);
            Assert.Empty(_store.Names(1));
        }

        [Fact]
        public async Task Another_failure_of_the_round_trip_passes_through_counted_as_failed()
        {
            _runner.Failure = new BatchAssertionFailedException(TellmaSqlErrors.Invariant, "Some.Other", 0);

            _ = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => Service().StageAsync(Request(File.Kind, Pdf(100), BlobContentTypes.Pdf), Token));

            AssertCounted(File.Kind, BlobTelemetryTagValues.UploadFailed);
        }

        [Fact]
        public async Task The_platforms_own_scope_stages_as_the_system_user_without_the_quota()
        {
            _context.Current = _context.Current with { Kind = PrincipalKind.System, Subject = "system", UserId = null };
            _runner.User = WellKnownIds.SystemUserId;

            _ = await Service().StageAsync(Request(File.Kind, Pdf(100), BlobContentTypes.Pdf), Token);

            Assert.Equal(WellKnownIds.SystemUserId, _runner.Parameters["p0"]);
            Assert.False(_runner.Parameters.ContainsKey("p11"));
            Assert.DoesNotContain("THROW", _runner.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_failing_store_leaves_the_row_staged_and_the_caller_never_sees_its_id()
        {
            _store.FailWrites = new BlobStoreException("The disk is full.", null);

            BlobStoreException failure = await Assert.ThrowsAsync<BlobStoreException>(
                () => Service().StageAsync(Request(File.Kind, Pdf(100), BlobContentTypes.Pdf), Token));

            Assert.Same(_store.FailWrites, failure);
            Assert.Equal(1, _runner.Runs);
            Assert.Empty(_store.Names(1));
            AssertCounted(File.Kind, BlobTelemetryTagValues.UploadFailed);
            Assert.Equal("BlobStoreWriteFailed", _logger.LatestRecord.Id.Name);
            Assert.Contains(" 7 ", _logger.LatestRecord.Message, StringComparison.Ordinal);
        }

        /// <summary>The running case's cancellation token.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The service over the suite's doubles and the stub processor.</summary>
        /// <param name="options">The options; the defaults when null.</param>
        /// <returns>The service.</returns>
        private BlobService Service(BlobOptions? options = null)
        {
            return Create(_processor, options);
        }

        /// <summary>The service over the suite's doubles and another processor, or none.</summary>
        /// <param name="processor">The processor; null for none.</param>
        /// <returns>The service.</returns>
        private BlobService Service(IImageProcessor? processor)
        {
            return Create(processor, null);
        }

        /// <summary>Creates the service.</summary>
        /// <param name="processor">The processor; null for none.</param>
        /// <param name="options">The options; the defaults when null.</param>
        /// <returns>The service.</returns>
        private BlobService Create(IImageProcessor? processor, BlobOptions? options)
        {
            return new BlobService(
                new Kinds(File, Photo, Small),
                _store,
                processor,
                _runner,
                _context,
                options ?? new BlobOptions(),
                new ScratchDirectory(_scratch),
                new BlobUploadMetrics(_meters.GetRequiredService<IMeterFactory>()),
                _logger);
        }

        /// <summary>Stages, expecting a refusal.</summary>
        /// <param name="request">The upload.</param>
        /// <param name="options">The options; the defaults when null.</param>
        /// <returns>The refusal.</returns>
        private async Task<BlobRejectedException> RefusedAsync(BlobStageRequest request, BlobOptions? options = null)
        {
            BlobRejectedException refused = await Assert.ThrowsAsync<BlobRejectedException>(() => Service(options).StageAsync(request, Token));
            Assert.Equal("BlobRejected", _logger.LatestRecord.Id.Name);
            if (refused.Code != BlobRejectedException.StagingQuotaExceeded)
            {
                AssertNothingStaged();
            }

            return refused;
        }

        /// <summary>Asserts that no round trip ran and nothing was written.</summary>
        private void AssertNothingStaged()
        {
            Assert.Equal(0, _runner.Runs);
            Assert.Empty(_store.Names(1));
        }

        /// <summary>Asserts that exactly one upload was counted, with its kind and outcome.</summary>
        /// <param name="kind">The kind.</param>
        /// <param name="outcome">The outcome.</param>
        private void AssertCounted(string kind, string outcome)
        {
            CollectedMeasurement<long> counted = Assert.Single(_uploads.GetMeasurementSnapshot());
            Assert.Equal((1L, kind, outcome), (counted.Value, counted.Tags[BlobTelemetryNames.KindTag], counted.Tags[BlobTelemetryNames.OutcomeTag]));
        }

        /// <summary>A collector of one upload instrument on the suite's meters.</summary>
        /// <typeparam name="T">The measurement type.</typeparam>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<T> Collector<T>(string instrument)
            where T : struct
        {
            return new MetricCollector<T>(_meters.GetRequiredService<IMeterFactory>(), BlobTelemetryNames.MeterName, instrument);
        }

        /// <summary>An upload of bytes.</summary>
        /// <param name="kind">The kind.</param>
        /// <param name="bytes">The bytes.</param>
        /// <param name="contentType">The declared type.</param>
        /// <param name="fileName">The file name.</param>
        /// <returns>The request.</returns>
        private static BlobStageRequest Request(string kind, byte[] bytes, string? contentType, string? fileName = null)
        {
            return new BlobStageRequest(kind, new MemoryStream(bytes, writable: false), bytes.LongLength, contentType, fileName);
        }

        /// <summary>A PDF of a length: its signature, then filler.</summary>
        /// <param name="length">The length.</param>
        /// <returns>The bytes.</returns>
        private static byte[] Pdf(int length)
        {
            byte[] bytes = new byte[length];
            bytes.AsSpan().Fill((byte)'x');
            Encoding.ASCII.GetBytes("%PDF-1.7\n").CopyTo(bytes, 0);
            return bytes;
        }

        /// <summary>A PNG of a length: its signature, then filler.</summary>
        /// <param name="length">The length.</param>
        /// <returns>The bytes.</returns>
        private static byte[] Png(int length)
        {
            byte[] bytes = new byte[length];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
            return bytes;
        }

        /// <summary>A kind owned by the album stack.</summary>
        /// <param name="kind">The kind.</param>
        /// <param name="policy">Its policy.</param>
        /// <returns>The descriptor.</returns>
        private static BlobKindDescriptor Descriptor(string kind, BlobKindPolicy policy)
        {
            return new BlobKindDescriptor(kind, policy, BlobModel.Albums, BlobModel.Albums, new TableName("bm", "Albums"), "FileId", string.Empty);
        }

        /// <summary>A registry of fixed kinds.</summary>
        /// <param name="kinds">The kinds.</param>
        private sealed class Kinds(params BlobKindDescriptor[] kinds) : IBlobKindRegistry
        {
            /// <inheritdoc />
            public IReadOnlyList<BlobKindDescriptor> All => kinds;

            /// <inheritdoc />
            public BlobKindDescriptor? Find(string kind)
            {
                return kinds.FirstOrDefault(descriptor => descriptor.Kind == kind);
            }
        }

        /// <summary>The scope's context: user 42 of tenant 1, unless a case changes it.</summary>
        private sealed class Holder : IRequestContextAccessor
        {
            /// <inheritdoc />
            public RequestContext Current { get; set; } = RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                Kind = PrincipalKind.User,
                Subject = "sub-42",
                UserId = 42,
            };
        }

        /// <summary>
        ///     A guarded runner that composes the batch for the connected user, records its statement,
        ///     and answers it with id 7 and the deadline, or fails as a case scripts.
        /// </summary>
        /// <param name="harness">The batches.</param>
        private sealed class StageRunner(BatchHarness harness) : IGuardedBatchRunner
        {
            /// <summary>The connected user's id the prologue would confirm.</summary>
            public int User { get; set; } = 42;

            /// <summary>The failure the round trip ends in, or null for success.</summary>
            public Exception? Failure { get; set; }

            /// <summary>How many round trips ran.</summary>
            public int Runs { get; private set; }

            /// <summary>The purpose of the last batch.</summary>
            public BatchPurpose? Purpose { get; private set; }

            /// <summary>The last statement's text.</summary>
            public string Text { get; private set; } = string.Empty;

            /// <summary>The last statement's parameter values by their suffix (<c>p0</c>, <c>p1</c>, …).</summary>
            public Dictionary<string, object?> Parameters { get; } = new(StringComparer.Ordinal);

            /// <inheritdoc />
            public async Task<TResult> RunAsync<TResult>(
                BatchPurpose purpose,
                Func<ConnectedUser, IDataBatch, ValueTask> compose,
                Func<ConnectedUser, BatchOutcome, TResult> read,
                CancellationToken cancellationToken = default)
            {
                Runs++;
                Purpose = purpose;
                ConnectedUser user = Connected(User);
                DataBatch batch = harness.Batch(purpose);
                await compose(user, batch);

                // --- What the statement would send ---
                BatchStatement statement = Assert.Single(batch.Statements);
                Text = statement.Text;
                Parameters.Clear();
                foreach (ScalarParameter parameter in statement.Parameters.Cast<ScalarParameter>())
                {
                    Parameters[parameter.Name[(parameter.Name.LastIndexOf('_') + 1)..]] = parameter.Value;
                }

                // --- What the database would answer ---
                if (Failure is not null)
                {
                    throw Failure;
                }

                await BlobStatementsTests.ReplayAsync(batch, BlobStatementsTests.Set((7, Expires)));
                return read(user, new BatchOutcome(new VersionTagSnapshot(1, new Dictionary<string, VersionTag>(), Now), null, 1));
            }

            /// <summary>A connected user.</summary>
            /// <param name="userId">The user's id.</param>
            /// <returns>The user.</returns>
            private static ConnectedUser Connected(int userId)
            {
                return new ConnectedUser(
                    userId,
                    UserKind.Individual,
                    UserState.Joined,
                    new UserProfile("Ada", null, null, null, null, null, null, null, null, null, null, null, null, null, Now),
                    Guid.Empty,
                    new UserAccess(),
                    new Dictionary<string, Guid>(StringComparer.Ordinal),
                    null);
            }
        }

        /// <summary>A processor that re-encodes every image to three fixed bytes of 1600 by 900, recording what it was asked.</summary>
        private sealed class RecodingProcessor : IImageProcessor
        {
            /// <summary>The primary it returns.</summary>
            public static readonly byte[] Output = [9, 9, 9];

            /// <summary>The thumbnail it returns.</summary>
            public static readonly byte[] Thumbnail = [7, 7];

            /// <summary>The kind it was asked for.</summary>
            public string? Kind { get; private set; }

            /// <summary>The policy it was asked for.</summary>
            public ImagePolicy? Policy { get; private set; }

            /// <summary>The budget it was given.</summary>
            public TimeSpan Timeout { get; private set; }

            /// <inheritdoc />
            public Task<ProcessedImage> ProcessAsync(
                string kind, byte[] input, ImagePolicy policy, ThumbnailPolicy? thumbnail, TimeSpan timeout, CancellationToken cancellationToken)
            {
                (Kind, Policy, Timeout) = (kind, policy, timeout);
                return Task.FromResult(new ProcessedImage(Output, 1600, 900, thumbnail is null ? null : Thumbnail, BlobContentTypes.WebP));
            }

            /// <inheritdoc />
            public Task<byte[]?> ThumbnailAsync(
                string kind, Stream input, string contentType, ThumbnailPolicy policy, TimeSpan timeout, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
