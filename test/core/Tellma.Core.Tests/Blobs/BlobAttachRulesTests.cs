// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Blobs;
using Tellma.Core.Crud;
using Tellma.Core.Data;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The attach rule's pure parts: the refusal of one blob row in its order of checks, the
    ///     aggregate's columns at every depth, the values a payload newly attaches on inserted and
    ///     updated rows and their children, whether the validator judges a payload at all, the refusal
    ///     instrument, the persist's refusal counted once per attached value, and a blob row gone
    ///     before the persist refused as not attachable.
    /// </summary>
    public sealed class BlobAttachRulesTests : IAsyncDisposable
    {
        /// <summary>The saving user.</summary>
        private const int Uploader = 42;

        /// <summary>The instant the save started.</summary>
        private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The blob suite's model, composed.</summary>
        private readonly ServiceProvider _model = TestComposition.Compose(static tellma => tellma.AddFeature(BlobModel.Feature()))
            .BuildServiceProvider(validateScopes: true);

        /// <summary>The meters the instrument is created on.</summary>
        private readonly ServiceProvider _meters = new ServiceCollection().AddMetrics().BuildServiceProvider();

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _model.DisposeAsync();
            await _meters.DisposeAsync();
        }

        [Fact]
        public void A_staged_unexpired_blob_of_the_kind_the_user_uploaded_is_attachable()
        {
            Assert.Null(BlobReferenceValidator<BmAlbum>.Refusal(Staged(), BlobModel.CoverKind, Uploader, Now));
        }

        [Fact]
        public void Each_check_refuses_with_its_reason_and_the_first_failing_one_wins()
        {
            // --- One failing check at a time ---
            Assert.Equal(BlobTelemetryTagValues.AttachMissing, BlobReferenceValidator<BmAlbum>.Refusal(null, BlobModel.CoverKind, Uploader, Now));
            Assert.Equal(BlobTelemetryTagValues.AttachNotStaged, Refusal(Staged(state: BlobState.Committed)));
            Assert.Equal(BlobTelemetryTagValues.AttachNotStaged, Refusal(Staged(state: BlobState.Released)));
            Assert.Equal(BlobTelemetryTagValues.AttachKind, Refusal(Staged(kind: BlobModel.ScanKind)));
            Assert.Equal(BlobTelemetryTagValues.AttachUploader, Refusal(Staged(uploader: Uploader + 1)));
            Assert.Equal(BlobTelemetryTagValues.AttachUploader, BlobReferenceValidator<BmAlbum>.Refusal(Staged(), BlobModel.CoverKind, null, Now));
            Assert.Equal(BlobTelemetryTagValues.AttachExpired, Refusal(Expiring(Staged(), Now)));
            Assert.Equal(BlobTelemetryTagValues.AttachExpired, Refusal(Expiring(Staged(), null)));

            // --- Several failing checks: the state before the kind, the kind before the uploader, the uploader before the deadline ---
            Assert.Equal(BlobTelemetryTagValues.AttachNotStaged, Refusal(Expiring(Staged(BlobState.Committed, BlobModel.ScanKind, uploader: 1), null)));
            Assert.Equal(BlobTelemetryTagValues.AttachKind, Refusal(Expiring(Staged(kind: BlobModel.ScanKind, uploader: 1), null)));
            Assert.Equal(BlobTelemetryTagValues.AttachUploader, Refusal(Expiring(Staged(uploader: 1), null)));
        }

        [Fact]
        public void The_aggregates_columns_are_the_roots_then_each_childs_at_every_depth()
        {
            IReadOnlyList<BlobColumn> columns = BlobAttachments.Columns(Metadata<BmAlbum>());

            Assert.Equal(
                ["bm.Albums.CoverId bm-cover", "bm.Pages.ScanId bm-scan", "bm.Stickers.ImageId bm-sticker"],
                columns.Select(static column => $"{column.Table.Schema}.{column.Table.Name}.{column.Column} {column.Kind}"));
            Assert.Same(columns, BlobAttachments.Columns(Metadata<BmAlbum>()));
        }

        [Fact]
        public void An_inserted_row_attaches_every_non_null_value_at_every_depth()
        {
            BmAlbum album = new()
            {
                CoverId = 1,
                Pages = [new BmPage { ScanId = 2 }, new BmPage { ScanId = null }, new BmPage { ScanId = 3 }],
                Spreads = [new BmSpread { Stickers = [new BmSticker { ImageId = 4 }] }],
            };

            Assert.Equal(
                ["[0].CoverId 1", "[0].Pages[0].ScanId 2", "[0].Pages[2].ScanId 3", "[0].Spreads[0].Stickers[0].ImageId 4"],
                Attached(album, before: null));
        }

        [Fact]
        public void The_validator_judges_a_payload_only_when_a_row_references_a_blob_at_some_depth()
        {
            BlobReferenceValidator<BmAlbum> validator = new(
                _model.GetRequiredService<IEntityMetadataProvider>(), new BlobAttachMetrics(_meters.GetRequiredService<IMeterFactory>()));

            // --- No reference anywhere: nothing for a permission set to change ---
            Assert.False(validator.Judges([new BmAlbum(), new BmAlbum { Pages = [new BmPage { ScanId = null }] }]));

            // --- A reference on a root, or on a child row two levels down ---
            Assert.True(validator.Judges([new BmAlbum(), new BmAlbum { CoverId = 1 }]));
            Assert.True(validator.Judges([new BmAlbum { Spreads = [new BmSpread { Stickers = [new BmSticker { ImageId = 4 }] }] }]));
        }

        [Fact]
        public void An_updated_row_attaches_a_changed_value_and_a_new_childs_and_leaves_an_unchanged_or_removed_one()
        {
            BmAlbum stored = new()
            {
                Id = 10,
                CoverId = 1,
                Pages = [new BmPage { Id = 20, ScanId = 2 }, new BmPage { Id = 21, ScanId = 3 }, new BmPage { Id = 22, ScanId = 5 }],
            };
            BmAlbum payload = new()
            {
                Id = 10,
                CoverId = 1,
                Pages = [new BmPage { Id = 20, ScanId = 2 }, new BmPage { Id = 21, ScanId = 6 }, new BmPage { ScanId = 7 }, new BmPage { Id = 22, ScanId = null }],
            };

            Assert.Equal(["[0].Pages[1].ScanId 6", "[0].Pages[2].ScanId 7"], Attached(payload, stored));

            // --- A root value replaced, a value set where the stored row had none ---
            payload.CoverId = 8;
            stored.CoverId = null;
            Assert.Equal(["[0].CoverId 8", "[0].Pages[1].ScanId 6", "[0].Pages[2].ScanId 7"], Attached(payload, stored));
        }

        [Fact]
        public void A_refused_attach_is_counted_once_with_its_kind_and_reason()
        {
            using MetricCollector<long> rejected = Collector();
            BlobAttachMetrics metrics = new(_meters.GetRequiredService<IMeterFactory>());

            metrics.Rejected(BlobModel.CoverKind, BlobTelemetryTagValues.AttachExpired);

            CollectedMeasurement<long> counted = Assert.Single(rejected.GetMeasurementSnapshot());
            Assert.Equal(
                (1L, BlobModel.CoverKind, BlobTelemetryTagValues.AttachExpired),
                (counted.Value, counted.Tags[BlobTelemetryNames.KindTag], counted.Tags[BlobTelemetryNames.ReasonTag]));
        }

        [Fact]
        public void A_persist_refusal_counts_every_attached_value_of_the_save_and_any_other_failure_counts_none()
        {
            using MetricCollector<long> rejected = Collector();
            BlobAttachMetrics metrics = new(_meters.GetRequiredService<IMeterFactory>());
            BmAlbum album = new() { CoverId = 1, Pages = [new BmPage { ScanId = 2 }] };
            (EntityMetadata, object, object?, ValidationPath)[] roots = [(Metadata<BmAlbum>(), album, null, ValidationPath.Root.Index(0))];

            // --- Another failure, then a refusal at the root that is not the persist's ---
            BlobAttachments.CountPersistRefusal(new InvalidOperationException("Not a refusal."), roots, metrics);
            BlobAttachments.CountPersistRefusal(Refused(BlobTelemetryTagValues.AttachExpired), roots, metrics);
            Assert.Empty(rejected.GetMeasurementSnapshot());

            // --- The persist's refusal: each attached value, by its column's kind ---
            BlobAttachments.CountPersistRefusal(Refused(BlobTelemetryTagValues.AttachPersist), roots, metrics);
            Assert.Equal(
                [$"{BlobModel.CoverKind} {BlobTelemetryTagValues.AttachPersist}", $"{BlobModel.ScanKind} {BlobTelemetryTagValues.AttachPersist}"],
                rejected.GetMeasurementSnapshot().Select(static measurement => $"{measurement.Tags[BlobTelemetryNames.KindTag]} {measurement.Tags[BlobTelemetryNames.ReasonTag]}"));
        }

        [Fact]
        public void A_blob_row_gone_before_the_persist_is_not_attachable_never_a_missing_reference()
        {
            // The write names a blob row that the sweep deleted after the validation round.
            BmAlbum album = new() { Name = "A", CoverId = 5 };
            BmAlbum other = new() { Name = "B", CoverId = 6 };
            CrudMetrics metrics = _model.GetRequiredService<CrudMetrics>();

            // --- One row carries a value: the refusal is at its property ---
            foreach (ForeignKeyConflict conflict in new[] { ForeignKeyConflict.MissingTarget, ForeignKeyConflict.Unknown })
            {
                Exception? one = PersistFailures.Translate(new ForeignKeyViolationException("FK_Albums_CoverId", statementOrdinal: 1) { Conflict = conflict }, Context(album), metrics);
                ValidationError error = Assert.Single(Assert.IsType<ValidationException>(one).Errors);
                Assert.Equal(("[0].CoverId", ValidationCodes.BlobNotAttachable, (object?)BlobTelemetryTagValues.AttachPersist), (error.Path.ToString(), error.Code, error.Arguments[BlobAttachments.ReasonArgument]));
            }

            // --- Several rows carry one: the refusal is at the root, naming the property ---
            Exception? several = PersistFailures.Translate(new ForeignKeyViolationException("FK_Albums_CoverId", statementOrdinal: 1) { Conflict = ForeignKeyConflict.MissingTarget }, Context(album, other), metrics);
            ValidationError root = Assert.Single(Assert.IsType<ValidationException>(several).Errors);
            Assert.Equal(
                (string.Empty, ValidationCodes.BlobNotAttachable, (object?)BlobTelemetryTagValues.AttachPersist, (object?)nameof(BmAlbum.CoverId)),
                (root.Path.ToString(), root.Code, root.Arguments[BlobAttachments.ReasonArgument], root.Arguments["property"]));
        }

        /// <summary>The refusal of a blob row for the cover kind, for the uploader, now.</summary>
        /// <param name="blob">The row.</param>
        /// <returns>The reason; null when attachable.</returns>
        private static string? Refusal(Blob blob)
        {
            return BlobReferenceValidator<BmAlbum>.Refusal(blob, BlobModel.CoverKind, Uploader, Now);
        }

        /// <summary>A blob row, attachable to the cover by the uploader unless told otherwise.</summary>
        /// <param name="state">The state.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="uploader">The uploader.</param>
        /// <returns>The row, due to expire a minute from now.</returns>
        private static Blob Staged(BlobState state = BlobState.Staged, string kind = BlobModel.CoverKind, int uploader = Uploader)
        {
            return new Blob { Id = 1, State = state, Kind = kind, CreatedById = uploader, ExpiresAt = Now.AddMinutes(1) };
        }

        /// <summary>A blob row with another deadline.</summary>
        /// <param name="blob">The row.</param>
        /// <param name="expires">The deadline; null for none.</param>
        /// <returns>The row.</returns>
        private static Blob Expiring(Blob blob, DateTimeOffset? expires)
        {
            blob.ExpiresAt = expires;
            return blob;
        }

        /// <summary>A validation refusal at the root naming the attach reason, as the persist's translation raises it.</summary>
        /// <param name="reason">The reason.</param>
        /// <returns>The refusal.</returns>
        private static ValidationException Refused(string reason)
        {
            return new ValidationException(
                [new ValidationError(ValidationPath.Root, ValidationCodes.BlobNotAttachable, new Dictionary<string, object?>(StringComparer.Ordinal) { [BlobAttachments.ReasonArgument] = reason })]);
        }

        /// <summary>The values a payload album newly attaches, each as its path and id.</summary>
        /// <param name="album">The payload album.</param>
        /// <param name="before">The stored album; null for an insert.</param>
        /// <returns>The lines.</returns>
        private string[] Attached(BmAlbum album, BmAlbum? before)
        {
            List<BlobAttach> attached = [];
            BlobAttachments.NewlyAttached(Metadata<BmAlbum>(), album, before, ValidationPath.Root.Index(0), attached);
            return [.. attached.Select(static attach => $"{attach.Path} {attach.Id}")];
        }

        /// <summary>What a save of albums knows about its persist.</summary>
        /// <param name="albums">The payload's albums, the only rows.</param>
        /// <returns>The context.</returns>
        private PersistFailureContext Context(params BmAlbum[] albums)
        {
            EntityMetadata root = Metadata<BmAlbum>();
            return new PersistFailureContext(BlobModel.Albums, AccessActions.Save, root, [.. albums.Select((album, index) => new PersistRow(root, album, ValidationPath.Root.Index(index)))], LanguageIndex: 1);
        }

        /// <summary>An entity's metadata in the composed model.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <returns>The metadata.</returns>
        private EntityMetadata Metadata<TEntity>()
        {
            return _model.GetRequiredService<IEntityMetadataProvider>().Get(typeof(TEntity));
        }

        /// <summary>A collector of the refusal instrument.</summary>
        /// <returns>The collector.</returns>
        private MetricCollector<long> Collector()
        {
            return new MetricCollector<long>(_meters.GetRequiredService<IMeterFactory>(), BlobTelemetryNames.MeterName, BlobTelemetryNames.AttachRejected);
        }
    }
}
