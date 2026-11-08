// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The blob telemetry's wire names, pinned: dashboards and alert queries are written against
    ///     these spellings, so a rename must fail here before it silently breaks them.
    /// </summary>
    public class BlobTelemetryNamesTests
    {
        [Fact]
        public void The_meters_and_instruments_keep_their_names()
        {
            Assert.Equal("Tellma.Core", BlobTelemetryNames.MeterName);
            Assert.Equal("Tellma.Blobs", BlobTelemetryNames.StoreMeterName);
            Pin(
                [
                    "tellma.blobs.uploads",
                    "tellma.blobs.upload.bytes",
                    "tellma.blobs.upload.duration",
                    "tellma.blobs.image.duration",
                    "tellma.blobs.image.failures",
                    "tellma.blobs.attach.rejected",
                    "tellma.blobs.downloads",
                    "tellma.blobs.store.operations",
                    "tellma.blobs.store.duration",
                    "tellma.blobs.sweep.deleted",
                    "tellma.blobs.sweep.failures",
                    "tellma.blobs.reconcile.released",
                ],
                [
                    BlobTelemetryNames.Uploads,
                    BlobTelemetryNames.UploadBytes,
                    BlobTelemetryNames.UploadDuration,
                    BlobTelemetryNames.ImageDuration,
                    BlobTelemetryNames.ImageFailures,
                    BlobTelemetryNames.AttachRejected,
                    BlobTelemetryNames.Downloads,
                    BlobTelemetryNames.StoreOperations,
                    BlobTelemetryNames.StoreDuration,
                    BlobTelemetryNames.SweepDeleted,
                    BlobTelemetryNames.SweepFailures,
                    BlobTelemetryNames.ReconcileReleased,
                ]);
        }

        [Fact]
        public void The_tag_keys_keep_their_names()
        {
            Pin(
                ["kind", "outcome", "operation", "state", "reason", "size_class"],
                [
                    BlobTelemetryNames.KindTag,
                    BlobTelemetryNames.OutcomeTag,
                    BlobTelemetryNames.OperationTag,
                    BlobTelemetryNames.StateTag,
                    BlobTelemetryNames.ReasonTag,
                    BlobTelemetryNames.SizeClassTag,
                ]);
        }

        [Fact]
        public void The_closed_tag_value_sets_keep_their_spellings()
        {
            Pin(
                ["staged", "rejected_size", "rejected_type", "rejected_image", "quota", "failed"],
                [
                    BlobTelemetryTagValues.UploadStaged,
                    BlobTelemetryTagValues.UploadRejectedSize,
                    BlobTelemetryTagValues.UploadRejectedType,
                    BlobTelemetryTagValues.UploadRejectedImage,
                    BlobTelemetryTagValues.UploadQuota,
                    BlobTelemetryTagValues.UploadFailed,
                ]);
            Pin(
                ["served", "not_modified", "not_found"],
                [BlobTelemetryTagValues.DownloadServed, BlobTelemetryTagValues.DownloadNotModified, BlobTelemetryTagValues.DownloadNotFound]);
            Pin(
                ["process", "thumbnail"],
                [BlobTelemetryTagValues.ImageProcess, BlobTelemetryTagValues.ImageThumbnail]);
            Pin(
                ["format", "too-many-pixels", "decode-failed", "timeout"],
                [
                    BlobTelemetryTagValues.ImageFormatReason,
                    BlobTelemetryTagValues.ImageTooManyPixelsReason,
                    BlobTelemetryTagValues.ImageDecodeFailedReason,
                    BlobTelemetryTagValues.ImageTimeoutReason,
                ]);
            Pin(
                ["missing", "not-staged", "kind", "uploader", "expired", "duplicate", "persist"],
                [
                    BlobTelemetryTagValues.AttachMissing,
                    BlobTelemetryTagValues.AttachNotStaged,
                    BlobTelemetryTagValues.AttachKind,
                    BlobTelemetryTagValues.AttachUploader,
                    BlobTelemetryTagValues.AttachExpired,
                    BlobTelemetryTagValues.AttachDuplicate,
                    BlobTelemetryTagValues.AttachPersist,
                ]);
            Pin(
                ["ensure", "write", "read", "delete", "list", "purge"],
                [
                    BlobTelemetryTagValues.StoreEnsure,
                    BlobTelemetryTagValues.StoreWrite,
                    BlobTelemetryTagValues.StoreRead,
                    BlobTelemetryTagValues.StoreDelete,
                    BlobTelemetryTagValues.StoreList,
                    BlobTelemetryTagValues.StorePurge,
                ]);
            Pin(
                ["ok", "conflict", "not_found", "failed"],
                [BlobTelemetryTagValues.StoreOk, BlobTelemetryTagValues.StoreConflict, BlobTelemetryTagValues.StoreNotFound, BlobTelemetryTagValues.StoreFailed]);
            Pin(
                ["staged", "released", "deleting"],
                [BlobTelemetryTagValues.StateStaged, BlobTelemetryTagValues.StateReleased, BlobTelemetryTagValues.StateDeleting]);
            Pin(
                ["under_1mib", "1_to_10mib", "over_10mib"],
                [BlobTelemetryTagValues.SizeUnder1MiB, BlobTelemetryTagValues.Size1To10MiB, BlobTelemetryTagValues.SizeOver10MiB]);
        }

        /// <summary>Asserts that each constant spells its pinned value, in order.</summary>
        /// <param name="expected">The pinned spellings.</param>
        /// <param name="actual">The constants.</param>
        private static void Pin(string[] expected, string[] actual)
        {
            Assert.Equal(expected, actual);
        }
    }
}
