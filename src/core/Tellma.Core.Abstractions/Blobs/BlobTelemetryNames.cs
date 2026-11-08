// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     The wire names of the platform's blob telemetry: the two meters, the instruments, their
    ///     units, and the tag keys they carry. <see cref="BlobTelemetryTagValues" /> spells the tag
    ///     values.
    /// </summary>
    /// <remarks>
    ///     The store instruments live on a meter of their own, <see cref="StoreMeterName" />, because
    ///     every store implementation emits them — the file-system store inside the runtime and the
    ///     Azure adapter outside it — and an adapter references only this assembly. Shared constants
    ///     keep the two halves of one instrument spelled alike. No instrument carries a tenant or a
    ///     user: every dimension is a small closed set, and <see cref="KindTag" /> is bounded by the
    ///     kinds a distribution registers.
    /// </remarks>
    public static class BlobTelemetryNames
    {
        /// <summary>The meter of the runtime's blob instruments: uploads, downloads, image processing, attach, sweep and reconcile.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>The meter of the store instruments, shared by every store implementation.</summary>
        public const string StoreMeterName = "Tellma.Blobs";

        /// <summary>Counter of uploads, by kind and outcome.</summary>
        public const string Uploads = "tellma.blobs.uploads";

        /// <summary>Counter of uploaded bytes, by kind.</summary>
        public const string UploadBytes = "tellma.blobs.upload.bytes";

        /// <summary>Histogram of upload durations in seconds, by kind and size class.</summary>
        public const string UploadDuration = "tellma.blobs.upload.duration";

        /// <summary>Histogram of image-processing durations in seconds, by kind and operation.</summary>
        public const string ImageDuration = "tellma.blobs.image.duration";

        /// <summary>Counter of image-processing failures, by kind, operation and reason.</summary>
        public const string ImageFailures = "tellma.blobs.image.failures";

        /// <summary>Counter of refused attaches, by kind and reason.</summary>
        public const string AttachRejected = "tellma.blobs.attach.rejected";

        /// <summary>Counter of download requests, by kind and outcome.</summary>
        public const string Downloads = "tellma.blobs.downloads";

        /// <summary>Counter of store calls, by operation and outcome.</summary>
        public const string StoreOperations = "tellma.blobs.store.operations";

        /// <summary>
        ///     Histogram of store-call durations in seconds, by operation, and by size class on a
        ///     write. A read's duration is the time to open the stream.
        /// </summary>
        public const string StoreDuration = "tellma.blobs.store.duration";

        /// <summary>Counter of blobs the sweep deleted, by the state they were claimed from.</summary>
        public const string SweepDeleted = "tellma.blobs.sweep.deleted";

        /// <summary>Counter of sweep failures.</summary>
        public const string SweepFailures = "tellma.blobs.sweep.failures";

        /// <summary>Counter of committed blobs the reconcile released because no owner row referenced them, by kind.</summary>
        public const string ReconcileReleased = "tellma.blobs.reconcile.released";

        /// <summary>Tag: the blob kind.</summary>
        public const string KindTag = "kind";

        /// <summary>Tag: how a call ended.</summary>
        public const string OutcomeTag = "outcome";

        /// <summary>Tag: which operation a measurement belongs to.</summary>
        public const string OperationTag = "operation";

        /// <summary>Tag: the state a blob was in.</summary>
        public const string StateTag = "state";

        /// <summary>Tag: why something failed or was refused.</summary>
        public const string ReasonTag = "reason";

        /// <summary>Tag: the size bucket of an upload's declared length or a write's total bytes.</summary>
        public const string SizeClassTag = "size_class";

        /// <summary>The unit of <see cref="Uploads" />.</summary>
        public const string UploadUnit = "{upload}";

        /// <summary>The unit of <see cref="Downloads" />.</summary>
        public const string DownloadUnit = "{download}";

        /// <summary>The unit of <see cref="StoreOperations" />.</summary>
        public const string OperationUnit = "{operation}";

        /// <summary>The unit of the failure and rejection counters.</summary>
        public const string FailureUnit = "{failure}";

        /// <summary>The unit of <see cref="SweepDeleted" /> and <see cref="ReconcileReleased" />.</summary>
        public const string BlobUnit = "{blob}";

        /// <summary>The unit of <see cref="UploadBytes" />.</summary>
        public const string ByteUnit = "By";

        /// <summary>The unit of the duration histograms.</summary>
        public const string SecondUnit = "s";
    }
}
