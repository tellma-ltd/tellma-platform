// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     The platform's image processor, on SkiaSharp, with PDF first-page thumbnails rendered by
    ///     PDFium through PDFtoImage.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ProcessAsync" /> normalises an upload: the bytes' signature must be JPEG,
    ///         PNG, GIF or WebP, and Skia's codec must agree; the header's width times height must not
    ///         exceed the policy's pixel cap, checked before any pixel is decoded; JPEG and WebP decode
    ///         at the smallest scale that still covers the output; only the first frame is decoded;
    ///         the encoded orientation is applied; the image is fitted (<see cref="ImageFit" />, never
    ///         upscaled) and re-encoded in the policy's format, which writes no metadata and keeps
    ///         alpha in WebP and PNG. The thumbnail is drawn from the normalised image.
    ///     </para>
    ///     <para>
    ///         <see cref="ThumbnailAsync" /> renders a stored file: an image under the default pixel
    ///         cap, or a PDF's first page. PDFium is not thread-safe, so PDF renders queue behind one
    ///         process-wide gate. The time budget covers the wait and the render; when it runs out the
    ///         call returns null at once, the input is closed to the native reader so a decode still
    ///         in flight stops at its next read, and a PDF render keeps the gate until its native call
    ///         returns.
    ///     </para>
    ///     <para>
    ///         The processor records <c>tellma.blobs.image.duration</c> and
    ///         <c>tellma.blobs.image.failures</c>, and writes the <c>BlobImageRender</c> debug event
    ///         immediately before native code first touches an input.
    ///     </para>
    /// </remarks>
    public sealed class SkiaImageProcessor : IImageProcessor
    {
        /// <summary>The code of every <see cref="ImageRejectedException" /> this processor throws.</summary>
        public const string RejectedCode = "Image.Rejected";

        /// <summary>The argument of a pixel-cap refusal that carries the declared width.</summary>
        public const string WidthArgument = "width";

        /// <summary>The argument of a pixel-cap refusal that carries the declared height.</summary>
        public const string HeightArgument = "height";

        /// <summary>The argument of a pixel-cap refusal that carries the cap.</summary>
        public const string MaxPixelsArgument = "maxPixels";

        private readonly ILogger _logger;
        private readonly ImagingMetrics _metrics;
        private readonly TimeProvider _timeProvider;
        private readonly IPdfRasterizer _pdf;

        /// <summary>Creates the processor.</summary>
        /// <param name="logger">The processor's logger.</param>
        /// <param name="meterFactory">The host's meter factory.</param>
        /// <param name="timeProvider">The clock the thumbnail time budget runs on.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SkiaImageProcessor(ILogger<SkiaImageProcessor> logger, IMeterFactory meterFactory, TimeProvider timeProvider)
            : this(logger, meterFactory, timeProvider, PdfiumRasterizer.Instance)
        {
        }

        /// <summary>Creates the processor over a given PDF rasterizer.</summary>
        /// <param name="logger">The processor's logger.</param>
        /// <param name="meterFactory">The host's meter factory.</param>
        /// <param name="timeProvider">The clock the thumbnail time budget runs on.</param>
        /// <param name="pdf">Renders PDF pages.</param>
        internal SkiaImageProcessor(ILogger logger, IMeterFactory meterFactory, TimeProvider timeProvider, IPdfRasterizer pdf)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(meterFactory);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(pdf);

            _logger = logger;
            _metrics = new ImagingMetrics(meterFactory);
            _timeProvider = timeProvider;
            _pdf = pdf;
        }

        /// <summary>
        ///     The process-wide gate PDF renders queue behind: PDFium keeps global state and is not
        ///     thread-safe, so one render runs at a time in the process, whichever processor started it.
        /// </summary>
        internal static SemaphoreSlim PdfRenderGate { get; } = new(1, 1);

        /// <inheritdoc />
        /// <exception cref="ArgumentException"><paramref name="kind" /> is blank, or a policy value is out of range.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="input" /> or <paramref name="policy" /> is null.</exception>
        public Task<ProcessedImage> ProcessAsync(
            string kind, byte[] input, ImagePolicy policy, ThumbnailPolicy? thumbnail, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(kind);
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(policy);
            Validate(policy);
            if (thumbnail is not null)
            {
                Validate(thumbnail);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<ProcessedImage>(cancellationToken);
            }

            const string operation = BlobTelemetryTagValues.ImageProcess;

            // Section: the managed gate. Bytes that are none of the four images never reach Skia.
            string? contentType = ImageSignatures.SniffImage(input);
            if (contentType is null)
            {
                var failure = ImageFailure.Format("The bytes are not a JPEG, PNG, GIF or WebP image.");
                RecordFailure(kind, operation, failure);
                return Task.FromException<ProcessedImage>(Reject(failure));
            }

            // Section: the native work, inline on the caller's thread. The caller's cancellation
            // closes the input to the decoder, so an abandoned upload stops decoding at its next read.
            ImagingLog.Render(_logger, kind, operation, contentType, input.LongLength);
            long started = _timeProvider.GetTimestamp();
            try
            {
                using var bytes = new MemoryStream(input, writable: false);
                using var gated = new GatedReadStream(bytes, cancellationToken);
                ProcessedImage result = SkiaPipeline.Process(gated, contentType, policy, thumbnail, cancellationToken);
                RecordDuration(kind, operation, started);
                return Task.FromResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<ProcessedImage>(cancellationToken);
            }
            catch (ImageFailure failure)
            {
                RecordDuration(kind, operation, started);
                RecordFailure(kind, operation, failure);
                return Task.FromException<ProcessedImage>(Reject(failure));
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException"><paramref name="kind" /> or <paramref name="contentType" /> is blank,
        ///     <paramref name="input" /> is not readable and seekable, or a policy value or the timeout is out of range.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="input" /> or <paramref name="policy" /> is null.</exception>
        public async Task<byte[]?> ThumbnailAsync(
            string kind, Stream input, string contentType, ThumbnailPolicy policy, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(kind);
            ArgumentNullException.ThrowIfNull(input);
            ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
            ArgumentNullException.ThrowIfNull(policy);
            Validate(policy);
            if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The time budget is positive or infinite.");
            }

            if (!input.CanRead || !input.CanSeek)
            {
                throw new ArgumentException("The input must be readable and seekable.", nameof(input));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Section: what can be rendered. Anything else is no thumbnail, and no failure.
            string type = ImageSignatures.Normalise(contentType);
            bool isPdf = type == BlobContentTypes.Pdf;
            if (!isPdf && !BlobContentTypes.Images.Contains(type))
            {
                return null;
            }

            const string operation = BlobTelemetryTagValues.ImageThumbnail;

            // Section: the time budget. It closes the gate of the input with the caller's token, so
            // both stop a native read; the caller's token alone surfaces as a cancellation.
            using CancellationTokenSource budget = timeout == Timeout.InfiniteTimeSpan
                ? new CancellationTokenSource()
                : new CancellationTokenSource(timeout, _timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
            CancellationToken token = linked.Token;

            var gated = new GatedReadStream(input, token);
            Task<byte[]>? work = null;
            long? started = null;
            try
            {
                // Section: the managed gate, as for uploads: the stored type must match the bytes.
                if (!SignatureMatches(gated, type))
                {
                    RecordFailure(kind, operation, ImageFailure.Format($"The bytes do not begin like {type}."));
                    return null;
                }

                if (isPdf)
                {
                    // Section: the PDF path. Wait for the process-wide gate within the budget, then
                    // render on the pool; the render releases the gate when PDFium returns, which may
                    // be after this call has given up on it.
                    await PdfRenderGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        ImagingLog.Render(_logger, kind, operation, type, gated.Length);
                        started = _timeProvider.GetTimestamp();
                        work = Task.Run(() => RenderPdf(gated, policy, token), CancellationToken.None);
                    }
                    catch
                    {
                        PdfRenderGate.Release();
                        throw;
                    }
                }
                else
                {
                    // Section: the image path, on the pool so the budget can end the wait.
                    ImagingLog.Render(_logger, kind, operation, type, gated.Length);
                    started = _timeProvider.GetTimestamp();
                    work = Task.Run(
                        () => SkiaPipeline.Thumbnail(gated, type, policy, ImagePolicy.DefaultMaxInputPixels, token),
                        CancellationToken.None);
                }

                byte[] thumbnail = await work.WaitAsync(token).ConfigureAwait(false);
                RecordDuration(kind, operation, started);
                return thumbnail;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
            {
                // Section: the budget ran out, waiting for the gate or rendering.
                RecordDuration(kind, operation, started);
                _metrics.RecordFailure(kind, operation, BlobTelemetryTagValues.ImageTimeoutReason);
                ImagingLog.Failed(_logger, null, kind, operation, BlobTelemetryTagValues.ImageTimeoutReason, "The render did not finish within its time budget.");
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (ImageFailure failure)
            {
                RecordDuration(kind, operation, started);
                RecordFailure(kind, operation, failure);
                return null;
            }
#pragma warning disable CA1031 // A thumbnail is optional: whatever a hostile file makes a library throw, the upload proceeds without one.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                RecordDuration(kind, operation, started);
                _metrics.RecordFailure(kind, operation, BlobTelemetryTagValues.ImageDecodeFailedReason);
                ImagingLog.UnexpectedThumbnailFailure(_logger, exception, kind, type);
                return null;
            }
            finally
            {
                // Section: no new read of the caller's stream once this call returns: disposing the
                // view closes its gate and leaves the caller's stream alone. A render still unwinding
                // has its outcome observed, so a late failure is never an unobserved task.
                gated.Dispose();
                if (work is { IsCompleted: false })
                {
                    _ = work.ContinueWith(
                        static task => _ = task.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
        }

        /// <summary>Renders a PDF's first page and encodes it, releasing the render gate when PDFium returns.</summary>
        /// <param name="input">The gated PDF.</param>
        /// <param name="policy">The thumbnail policy.</param>
        /// <param name="token">The call's budget and cancellation.</param>
        /// <returns>The encoded thumbnail.</returns>
        private byte[] RenderPdf(GatedReadStream input, ThumbnailPolicy policy, CancellationToken token)
        {
            SkiaSharp.SKBitmap bitmap;
            try
            {
                // A budget that ran out while this task was being scheduled ends it before PDFium.
                token.ThrowIfCancellationRequested();
                bitmap = _pdf.RenderFirstPage(input, policy.Size);
            }
            finally
            {
                PdfRenderGate.Release();
            }

            using (bitmap)
            {
                token.ThrowIfCancellationRequested();
                return SkiaPipeline.EncodeBitmap(bitmap, policy.Format);
            }
        }

        /// <summary>Reads the input's first bytes, in managed code, and checks them against the stored type.</summary>
        /// <param name="input">The gated input; rewound afterwards.</param>
        /// <param name="type">The stored media type, an image type or PDF.</param>
        /// <returns>True when the bytes begin like <paramref name="type" />.</returns>
        private static bool SignatureMatches(GatedReadStream input, string type)
        {
            Span<byte> head = stackalloc byte[ImageSignatures.Length];
            int length = 0;
            while (length < head.Length)
            {
                int read = input.Read(head[length..]);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            input.Position = 0;
            ReadOnlySpan<byte> bytes = head[..length];
            return type == BlobContentTypes.Pdf ? ImageSignatures.IsPdf(bytes) : ImageSignatures.SniffImage(bytes) == type;
        }

        /// <summary>The rejection an upload's failure surfaces as.</summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The exception.</returns>
        private static ImageRejectedException Reject(ImageFailure failure)
        {
            var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ImageRejectedException.ReasonArgument] = failure.Reason,
            };

            if (failure.MaxPixels is long maxPixels)
            {
                arguments[WidthArgument] = failure.Width;
                arguments[HeightArgument] = failure.Height;
                arguments[MaxPixelsArgument] = maxPixels;
            }

            return new ImageRejectedException(RejectedCode, arguments);
        }

        /// <summary>Checks an image policy's values.</summary>
        /// <param name="policy">The policy.</param>
        private static void Validate(ImagePolicy policy)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.MaxDimension, nameof(policy));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.MaxInputPixels, nameof(policy));
            if (!Enum.IsDefined(policy.Fit) || !Enum.IsDefined(policy.Format))
            {
                throw new ArgumentException("The image policy names an undefined fit or format.", nameof(policy));
            }
        }

        /// <summary>Checks a thumbnail policy's values.</summary>
        /// <param name="policy">The policy.</param>
        private static void Validate(ThumbnailPolicy policy)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.Size, nameof(policy));
            if (!Enum.IsDefined(policy.Format))
            {
                throw new ArgumentException("The thumbnail policy names an undefined format.", nameof(policy));
            }
        }

        /// <summary>Records an operation's duration from its start, when native work started at all.</summary>
        /// <param name="kind">The blob kind.</param>
        /// <param name="operation">The operation.</param>
        /// <param name="started">The start timestamp, or null when the operation ended before native work.</param>
        private void RecordDuration(string kind, string operation, long? started)
        {
            if (started is long start)
            {
                _metrics.RecordDuration(kind, operation, _timeProvider.GetElapsedTime(start).TotalSeconds);
            }
        }

        /// <summary>Counts and logs a failure.</summary>
        /// <param name="kind">The blob kind.</param>
        /// <param name="operation">The operation.</param>
        /// <param name="failure">The failure.</param>
        private void RecordFailure(string kind, string operation, ImageFailure failure)
        {
            _metrics.RecordFailure(kind, operation, failure.Reason);
            ImagingLog.Failed(_logger, failure.InnerException, kind, operation, failure.Reason, failure.Message);
        }
    }
}
