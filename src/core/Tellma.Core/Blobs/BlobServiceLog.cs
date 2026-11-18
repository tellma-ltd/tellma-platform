// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Blobs
{
    /// <summary>Source-generated log messages of blob staging.</summary>
    /// <remarks>
    ///     The events carry the kind, the id and sizes, never file contents or the client's file name;
    ///     the tenant rides the log scope of the request or job.
    /// </remarks>
    internal static partial class BlobServiceLog
    {
        /// <summary>An upload was staged and its objects written.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="kind">The blob's kind.</param>
        /// <param name="id">The blob's id.</param>
        /// <param name="size">The stored primary's length in bytes.</param>
        /// <param name="contentType">The stored primary's media type.</param>
        /// <param name="durationMs">How long the upload took, in milliseconds.</param>
        [LoggerMessage(
            EventName = "BlobStaged",
            Level = LogLevel.Information,
            Message = "A blob of kind {Kind} was staged as {Id}: {Size} bytes of {ContentType} in {DurationMs} ms.")]
        public static partial void Staged(ILogger logger, string kind, int id, long size, string contentType, long durationMs);

        /// <summary>An upload was refused before anything was stored.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="kind">The kind the upload named.</param>
        /// <param name="code">The refusal's code.</param>
        [LoggerMessage(
            EventName = "BlobRejected",
            Level = LogLevel.Information,
            Message = "An upload of kind {Kind} was refused: {Code}.")]
        public static partial void Rejected(ILogger logger, string kind, string code);

        /// <summary>
        ///     The store failed to write a staged blob's objects; the row stays staged and expires into
        ///     the sweep.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The store's failure.</param>
        /// <param name="kind">The blob's kind.</param>
        /// <param name="id">The staged row's id.</param>
        [LoggerMessage(
            EventName = "BlobStoreWriteFailed",
            Level = LogLevel.Error,
            Message = "Writing the objects of the blob {Id} of kind {Kind} failed; its staged row expires into the sweep.")]
        public static partial void StoreWriteFailed(ILogger logger, Exception exception, string kind, int id);
    }
}
