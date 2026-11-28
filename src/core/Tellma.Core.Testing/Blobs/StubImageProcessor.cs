// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Testing.Blobs
{
    /// <summary>
    ///     An <see cref="IImageProcessor" /> that decodes nothing: it returns the input as the
    ///     normalised primary, sized to the policy's largest dimension and labelled with the policy's
    ///     format, and a fixed thumbnail. For test hosts whose composition declares image kinds and
    ///     tests that script the processor's refusals.
    /// </summary>
    /// <remarks>
    ///     Thread-safe for concurrent calls once configured. <see cref="RejectReason" /> and
    ///     <see cref="Busy" /> script the refusals of <see cref="ProcessAsync" />;
    ///     <see cref="RenderThumbnails" /> scripts whether <see cref="ThumbnailAsync" /> renders.
    /// </remarks>
    public sealed class StubImageProcessor : IImageProcessor
    {
        /// <summary>The code of the refusals it throws.</summary>
        public const string RejectedCode = "Stub.ImageRejected";

        // Counts of the calls, for assertions.
        private int _processCalls;
        private int _thumbnailCalls;

        /// <summary>The bytes of every thumbnail it renders.</summary>
        public static IReadOnlyList<byte> ThumbnailBytes { get; } = [0x54, 0x48, 0x55, 0x4D, 0x42];

        /// <summary>
        ///     When set, <see cref="ProcessAsync" /> throws <see cref="ImageRejectedException" /> with
        ///     this reason; null (the default) accepts every input.
        /// </summary>
        public string? RejectReason { get; set; }

        /// <summary>When true, <see cref="ProcessAsync" /> throws <see cref="ImageProcessingBusyException" />.</summary>
        public bool Busy { get; set; }

        /// <summary>Whether <see cref="ThumbnailAsync" /> renders a thumbnail; when false it returns null. True by default.</summary>
        public bool RenderThumbnails { get; set; } = true;

        /// <summary>How many times <see cref="ProcessAsync" /> was called.</summary>
        public int ProcessCalls => Volatile.Read(ref _processCalls);

        /// <summary>How many times <see cref="ThumbnailAsync" /> was called.</summary>
        public int ThumbnailCalls => Volatile.Read(ref _thumbnailCalls);

        /// <inheritdoc />
        public Task<ProcessedImage> ProcessAsync(
            string kind, byte[] input, ImagePolicy policy, ThumbnailPolicy? thumbnail, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(policy);
            cancellationToken.ThrowIfCancellationRequested();
            _ = Interlocked.Increment(ref _processCalls);

            // --- The scripted refusals ---
            if (Busy)
            {
                throw new ImageProcessingBusyException(TimeSpan.FromSeconds(1));
            }

            if (RejectReason is string reason)
            {
                throw new ImageRejectedException(RejectedCode, new Dictionary<string, object?> { [ImageRejectedException.ReasonArgument] = reason });
            }

            // --- The input as the primary, a fresh copy so the caller's buffer stays its own ---
            return Task.FromResult(new ProcessedImage(
                [.. input],
                policy.MaxDimension,
                policy.MaxDimension,
                thumbnail is null ? null : [.. ThumbnailBytes],
                BlobContentTypes.ForImageFormat(policy.Format)));
        }

        /// <inheritdoc />
        public Task<byte[]?> ThumbnailAsync(
            string kind, Stream input, string contentType, ThumbnailPolicy policy, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(policy);
            cancellationToken.ThrowIfCancellationRequested();
            _ = Interlocked.Increment(ref _thumbnailCalls);
            return Task.FromResult<byte[]?>(RenderThumbnails ? [.. ThumbnailBytes] : null);
        }
    }
}
