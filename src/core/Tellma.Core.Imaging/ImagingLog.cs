// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Imaging
{
    /// <summary>Source-generated log messages of the image processor.</summary>
    /// <remarks>
    ///     Messages carry the kind, the operation, the media type and sizes, never file names or
    ///     bytes. The tenant comes from the caller's logging scope.
    /// </remarks>
    internal static partial class ImagingLog
    {
        /// <summary>
        ///     Written immediately before native code first touches an input, so that a process that
        ///     dies inside a decoder is attributable to the last such line.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="kind">The blob kind.</param>
        /// <param name="operation"><c>process</c> or <c>thumbnail</c>.</param>
        /// <param name="contentType">The input's media type.</param>
        /// <param name="size">The input's length in bytes.</param>
        [LoggerMessage(
            EventName = "BlobImageRender",
            Level = LogLevel.Debug,
            Message = "Decoding a {ContentType} input of {Size} bytes for the {Operation} of a {Kind} blob.")]
        public static partial void Render(ILogger logger, string kind, string operation, string contentType, long size);

        /// <summary>An input was refused or could not be rendered; the failure counter counts it too.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The library's exception, when one caused the failure.</param>
        /// <param name="kind">The blob kind.</param>
        /// <param name="operation"><c>process</c> or <c>thumbnail</c>.</param>
        /// <param name="reason">The failure reason.</param>
        /// <param name="detail">What failed.</param>
        [LoggerMessage(
            EventName = "BlobImageFailed",
            Level = LogLevel.Debug,
            Message = "The {Operation} of a {Kind} blob failed ({Reason}): {Detail}")]
        public static partial void Failed(ILogger logger, Exception? exception, string kind, string operation, string reason, string detail);

        /// <summary>
        ///     An operation threw an exception the processor does not anticipate: an upload is then
        ///     refused as undecodable, and a thumbnail is not stored.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="kind">The blob kind.</param>
        /// <param name="operation"><c>process</c> or <c>thumbnail</c>.</param>
        /// <param name="contentType">The input's media type.</param>
        [LoggerMessage(
            EventName = "BlobImageUnexpectedFailure",
            Level = LogLevel.Warning,
            Message = "The {Operation} of a {ContentType} input of a {Kind} blob failed unexpectedly.")]
        public static partial void UnexpectedFailure(ILogger logger, Exception exception, string kind, string operation, string contentType);

        /// <summary>
        ///     A PDF worker failed rather than refusing the document: it could not start, ended
        ///     before it answered (crashed, or was stopped by its memory or CPU limit), broke the
        ///     protocol, or reported its own failure. The thumbnail is not stored; the upload and the
        ///     host are unaffected. Frequent events point at a hostile upload, a PDFium bug, or limits
        ///     too tight for honest documents.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="kind">The blob kind.</param>
        /// <param name="failure">How the worker failed: <c>Start</c>, <c>Exited</c>, <c>Protocol</c> or <c>Failed</c>.</param>
        /// <param name="exitCode">The worker's exit code, when it had exited.</param>
        /// <param name="detail">What failed.</param>
        [LoggerMessage(
            EventName = "BlobPdfWorkerFailed",
            Level = LogLevel.Warning,
            Message = "The PDF worker rendering a {Kind} thumbnail failed ({Failure}, exit code {ExitCode}): {Detail}")]
        public static partial void PdfWorkerFailed(ILogger logger, Exception exception, string kind, string failure, int? exitCode, string detail);

        /// <summary>No image slot became free within the budget: the server is busy, the upload is not at fault.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="kind">The blob kind.</param>
        /// <param name="concurrency">How many image operations may run at once.</param>
        [LoggerMessage(
            EventName = "BlobImageBusy",
            Level = LogLevel.Warning,
            Message = "A {Kind} upload found all {Concurrency} image slots busy for its whole time budget; it was refused as busy.")]
        public static partial void Busy(ILogger logger, string kind, int concurrency);

        /// <summary>
        ///     Written once, at startup, by a host that renders no PDF thumbnails: a host on Windows
        ///     or macOS outside Development, or a Linux host whose C library is not glibc. PDF uploads
        ///     proceed without a thumbnail, and no worker is started.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="reason">Why, as one sentence.</param>
        [LoggerMessage(
            EventName = "BlobPdfThumbnailsDisabled",
            Level = LogLevel.Warning,
            Message = "PDF thumbnails are disabled on this host; PDF uploads proceed without one. {Reason}")]
        public static partial void PdfThumbnailsDisabled(ILogger logger, string reason);

        /// <summary>
        ///     Written once, at startup, by a Linux host whose kernel lets a process open the memory
        ///     of any other process of its account: a PDF worker taken over by a hostile document
        ///     could then read and write the host's memory through <c>/proc</c>. PDF thumbnails still
        ///     render; the fix is the machine's.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="detail">What the kernel reports: the scope is 0, or Yama is not enabled.</param>
        [LoggerMessage(
            EventName = "BlobPdfWorkerPtraceUnrestricted",
            Level = LogLevel.Warning,
            Message = "The kernel lets a process open the memory of any other process of its account ({Detail}), so a PDF worker taken over by a hostile document could read and write this host's memory. Set the kernel.yama.ptrace_scope sysctl to 1 or higher on the machine that runs this host.")]
        public static partial void PtraceUnrestricted(ILogger logger, string detail);
    }
}
