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

        /// <summary>A thumbnail render threw an exception the processor does not anticipate; no thumbnail is stored.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="kind">The blob kind.</param>
        /// <param name="contentType">The input's media type.</param>
        [LoggerMessage(
            EventName = "BlobThumbnailUnexpectedFailure",
            Level = LogLevel.Warning,
            Message = "A thumbnail of a {ContentType} input of a {Kind} blob failed unexpectedly; no thumbnail is stored.")]
        public static partial void UnexpectedThumbnailFailure(ILogger logger, Exception exception, string kind, string contentType);
    }
}
