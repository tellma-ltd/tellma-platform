// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     The closed sets of values the blob telemetry tags take, spelled once so the runtime and
    ///     every store adapter emit them alike, and alert queries can be checked against them.
    /// </summary>
    /// <remarks>
    ///     Values are spelled out rather than derived from enum member names: a rename in code must
    ///     never rewrite a dimension that dashboards and alerts are written against.
    /// </remarks>
    public static class BlobTelemetryTagValues
    {
        // The size-class boundaries, in bytes.
        private const long OneMebibyte = 1024L * 1024;
        private const long TenMebibytes = 10 * OneMebibyte;

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of an upload: staged.</summary>
        public const string UploadStaged = "staged";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of an upload: refused for its size.</summary>
        public const string UploadRejectedSize = "rejected_size";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of an upload: refused for its media type.</summary>
        public const string UploadRejectedType = "rejected_type";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of an upload: refused by the image processor.</summary>
        public const string UploadRejectedImage = "rejected_image";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of an upload: refused by the staging quota.</summary>
        public const string UploadQuota = "quota";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of an upload: failed (a store or database failure).</summary>
        public const string UploadFailed = "failed";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of a download: bytes served.</summary>
        public const string DownloadServed = "served";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of a download: a matching entity tag, nothing served.</summary>
        public const string DownloadNotModified = "not_modified";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of a download: nothing to serve.</summary>
        public const string DownloadNotFound = "not_found";

        /// <summary><see cref="BlobTelemetryNames.OperationTag" /> of image processing: normalising an upload.</summary>
        public const string ImageProcess = "process";

        /// <summary><see cref="BlobTelemetryNames.OperationTag" /> of image processing: rendering a thumbnail.</summary>
        public const string ImageThumbnail = "thumbnail";

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of an image failure: not an accepted format.</summary>
        public const string ImageFormatReason = ImageRejectedException.FormatReason;

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of an image failure: over the pixel cap.</summary>
        public const string ImageTooManyPixelsReason = ImageRejectedException.TooManyPixelsReason;

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of an image failure: undecodable.</summary>
        public const string ImageDecodeFailedReason = ImageRejectedException.DecodeFailedReason;

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of an image failure: a render past its time budget.</summary>
        public const string ImageTimeoutReason = "timeout";

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of a refused attach: no such blob.</summary>
        public const string AttachMissing = "missing";

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of a refused attach: the blob is not staged.</summary>
        public const string AttachNotStaged = "not-staged";

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of a refused attach: the blob is of another kind.</summary>
        public const string AttachKind = "kind";

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of a refused attach: another user staged the blob.</summary>
        public const string AttachUploader = "uploader";

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of a refused attach: the staging deadline passed.</summary>
        public const string AttachExpired = "expired";

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of a refused attach: the payload uses the blob twice.</summary>
        public const string AttachDuplicate = "duplicate";

        /// <summary><see cref="BlobTelemetryNames.ReasonTag" /> of a refused attach: the persist-time re-check failed.</summary>
        public const string AttachPersist = "persist";

        /// <summary><see cref="BlobTelemetryNames.OperationTag" /> of a store call: ensuring a tenant's storage.</summary>
        public const string StoreEnsure = "ensure";

        /// <summary><see cref="BlobTelemetryNames.OperationTag" /> of a store call: writing objects.</summary>
        public const string StoreWrite = "write";

        /// <summary><see cref="BlobTelemetryNames.OperationTag" /> of a store call: opening an object.</summary>
        public const string StoreRead = "read";

        /// <summary><see cref="BlobTelemetryNames.OperationTag" /> of a store call: deleting objects.</summary>
        public const string StoreDelete = "delete";

        /// <summary><see cref="BlobTelemetryNames.OperationTag" /> of a store call: listing objects.</summary>
        public const string StoreList = "list";

        /// <summary><see cref="BlobTelemetryNames.OperationTag" /> of a store call: purging incomplete artefacts.</summary>
        public const string StorePurge = "purge";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of a store call: it succeeded.</summary>
        public const string StoreOk = "ok";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of a store call: a create-only write met an existing object.</summary>
        public const string StoreConflict = "conflict";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of a store call: a read found no object.</summary>
        public const string StoreNotFound = "not_found";

        /// <summary><see cref="BlobTelemetryNames.OutcomeTag" /> of a store call: the storage failed.</summary>
        public const string StoreFailed = "failed";

        /// <summary><see cref="BlobTelemetryNames.StateTag" />: a staged blob whose deadline passed.</summary>
        public const string StateStaged = "staged";

        /// <summary><see cref="BlobTelemetryNames.StateTag" />: a released blob.</summary>
        public const string StateReleased = "released";

        /// <summary><see cref="BlobTelemetryNames.StateTag" />: a blob re-claimed after an earlier claim lapsed.</summary>
        public const string StateDeleting = "deleting";

        /// <summary><see cref="BlobTelemetryNames.SizeClassTag" />: under 1 MiB.</summary>
        public const string SizeUnder1MiB = "under_1mib";

        /// <summary><see cref="BlobTelemetryNames.SizeClassTag" />: 1 MiB up to and including 10 MiB.</summary>
        public const string Size1To10MiB = "1_to_10mib";

        /// <summary><see cref="BlobTelemetryNames.SizeClassTag" />: over 10 MiB.</summary>
        public const string SizeOver10MiB = "over_10mib";

        /// <summary>The <see cref="BlobTelemetryNames.SizeClassTag" /> value for a length in bytes.</summary>
        /// <param name="bytes">The upload's declared length, or a write's total bytes.</param>
        /// <returns><see cref="SizeUnder1MiB" />, <see cref="Size1To10MiB" /> or <see cref="SizeOver10MiB" />.</returns>
        public static string SizeClass(long bytes)
        {
            return bytes switch
            {
                < OneMebibyte => SizeUnder1MiB,
                <= TenMebibytes => Size1To10MiB,
                _ => SizeOver10MiB,
            };
        }
    }
}
