// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
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
    ///         cap, or a PDF's first page once a walk of its objects has found nothing over the caps.
    ///     </para>
    ///     <para>
    ///         Native work cannot be stopped mid-call, so it runs behind gates that are released only
    ///         when the native work ends: image work behind
    ///         <see cref="SkiaImageProcessorOptions.ImageConcurrency" /> slots, and PDF work behind one
    ///         process-wide gate, because PDFium is not thread-safe. A call's budget bounds its wait and
    ///         its work; when it runs out the call returns at once, and the input is closed to the
    ///         native reader, so a decode still in flight stops at its next read. A PDF render inside
    ///         PDFium keeps its gate until it returns, and while it does, new PDF thumbnails fail fast.
    ///     </para>
    ///     <para>
    ///         The processor records <c>tellma.blobs.image.duration</c> and
    ///         <c>tellma.blobs.image.failures</c>, and writes the <c>BlobImageRender</c> debug event
    ///         immediately before native code first touches an input.
    ///     </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1001:Types that own disposable fields should be disposable",
        Justification = "The slot semaphore must outlive the processor: work abandoned at shutdown still releases its slot when its native part ends, and a semaphore that never hands out its wait handle holds no unmanaged resource.")]
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
        private readonly Func<byte[], Stream> _openInput;
        private readonly int _concurrency;

        // The image-work slots. Never disposed: a decode abandoned at shutdown still releases its
        // slot when it ends, and the semaphore holds no handle unless one is asked for.
        private readonly SemaphoreSlim _imageSlots;

        /// <summary>Creates the processor.</summary>
        /// <param name="logger">The processor's logger.</param>
        /// <param name="meterFactory">The host's meter factory.</param>
        /// <param name="timeProvider">The clock the time budgets run on.</param>
        /// <param name="options">The processor's options.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The concurrency is not positive.</exception>
        public SkiaImageProcessor(
            ILogger<SkiaImageProcessor> logger, IMeterFactory meterFactory, TimeProvider timeProvider, IOptions<SkiaImageProcessorOptions> options)
            : this(logger, meterFactory, timeProvider, (options ?? throw new ArgumentNullException(nameof(options))).Value, PdfiumRasterizer.Instance)
        {
        }

        /// <summary>Creates the processor over given seams.</summary>
        /// <param name="logger">The processor's logger.</param>
        /// <param name="meterFactory">The host's meter factory.</param>
        /// <param name="timeProvider">The clock the time budgets run on.</param>
        /// <param name="options">The processor's options.</param>
        /// <param name="pdf">Renders PDF pages.</param>
        /// <param name="openInput">Opens an upload's bytes for the decoder; an in-memory stream when null.</param>
        internal SkiaImageProcessor(
            ILogger logger,
            IMeterFactory meterFactory,
            TimeProvider timeProvider,
            SkiaImageProcessorOptions options,
            IPdfRasterizer pdf,
            Func<byte[], Stream>? openInput = null)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(meterFactory);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(pdf);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ImageConcurrency, nameof(options));

            _logger = logger;
            _metrics = new ImagingMetrics(meterFactory);
            _timeProvider = timeProvider;
            _pdf = pdf;
            _openInput = openInput ?? (static bytes => new MemoryStream(bytes, writable: false));
            _concurrency = options.ImageConcurrency;
            _imageSlots = new SemaphoreSlim(_concurrency, _concurrency);
        }

        /// <summary>How many image slots are free now; for the suite.</summary>
        internal int FreeImageSlots => _imageSlots.CurrentCount;

        /// <inheritdoc />
        /// <exception cref="ArgumentException"><paramref name="kind" /> is blank, or a policy value or the timeout is out of range.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="input" /> or <paramref name="policy" /> is null.</exception>
        public async Task<ProcessedImage> ProcessAsync(
            string kind, byte[] input, ImagePolicy policy, ThumbnailPolicy? thumbnail, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(kind);
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(policy);
            Validate(policy);
            if (thumbnail is not null)
            {
                Validate(thumbnail);
            }

            ValidateTimeout(timeout);
            cancellationToken.ThrowIfCancellationRequested();

            const string operation = BlobTelemetryTagValues.ImageProcess;

            // Section: the managed gate. Bytes that are none of the four images never reach Skia.
            string? contentType = ImageSignatures.SniffImage(input);
            if (contentType is null)
            {
                var failure = ImageFailure.Format("The bytes are not a JPEG, PNG, GIF or WebP image.");
                RecordFailure(kind, operation, failure);
                throw Reject(failure);
            }

            // Section: a slot within the budget, or the server is busy; that is not the upload's fault.
            using (CancellationTokenSource waitBudget = Budget(timeout))
            using (var waitToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, waitBudget.Token))
            {
                try
                {
                    await _imageSlots.WaitAsync(waitToken.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && waitBudget.IsCancellationRequested)
                {
                    ImagingLog.Busy(_logger, kind, _concurrency);
                    throw new ImageProcessingBusyException(timeout);
                }
            }

            // Section: the work, on the pool under its own budget. The slot is released by the work
            // itself when its native part ends, which may be after this call has given up on it.
            using CancellationTokenSource workBudget = Budget(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, workBudget.Token);
            CancellationToken token = linked.Token;
            Stream bytes;
            GatedReadStream gated;
            try
            {
                bytes = _openInput(input);
                gated = new GatedReadStream(bytes, token);
            }
            catch
            {
                _imageSlots.Release();
                throw;
            }

            ImagingLog.Render(_logger, kind, operation, contentType, input.LongLength);
            long started = _timeProvider.GetTimestamp();
            Task<ProcessedImage> work = RunInSlot(() => SkiaPipeline.Process(gated, contentType, policy, thumbnail, token));
            try
            {
                ProcessedImage result = await work.WaitAsync(token).ConfigureAwait(false);
                RecordDuration(kind, operation, started);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (OperationCanceledException) when (workBudget.IsCancellationRequested)
            {
                // The input is at fault: within the pixel cap, it still cost more than its budget.
                RecordDuration(kind, operation, started);
                var failure = ImageFailure.Timeout();
                RecordFailure(kind, operation, failure);
                throw Reject(failure);
            }
            catch (ImageFailure failure)
            {
                RecordDuration(kind, operation, started);
                RecordFailure(kind, operation, failure);
                throw Reject(failure);
            }
#pragma warning disable CA1031 // Whatever a hostile file makes the library throw is refused as undecodable, never a server error.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                RecordDuration(kind, operation, started);
                _metrics.RecordFailure(kind, operation, BlobTelemetryTagValues.ImageDecodeFailedReason);
                ImagingLog.UnexpectedFailure(_logger, exception, kind, operation, contentType);
                throw Reject(ImageFailure.DecodeFailed("The image could not be decoded.", exception));
            }
            finally
            {
                // Section: from here on the input is never touched: closing the view waits for a
                // read in flight and refuses every later one.
                gated.Dispose();
                await bytes.DisposeAsync().ConfigureAwait(false);
                Observe(work);
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
            ValidateTimeout(timeout);
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

            // Section: the time budget, covering the wait for a gate and the render. It closes the
            // input to the native reader as the caller's token does; the caller's token alone
            // surfaces as a cancellation.
            using CancellationTokenSource budget = Budget(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
            CancellationToken token = linked.Token;

            var gated = new GatedReadStream(input, token);
            Task<byte[]>? work = null;
            PdfRenderGate.Render? render = null;
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
                    // Section: the PDF path. A file over the input cap is not read at all, and a gate
                    // held by a render nobody waits for any more is not waited on.
                    if (gated.Length > PdfPreflight.MaxInputBytes)
                    {
                        RecordFailure(kind, operation, ImageFailure.DecodeFailed($"The PDF is larger than {PdfPreflight.MaxInputBytes} bytes."));
                        return null;
                    }

                    if (PdfRenderGate.IsHeldByAbandonedRender)
                    {
                        RecordFailure(kind, operation, new ImageFailure(BlobTelemetryTagValues.ImageTimeoutReason, "A render whose caller gave up still holds the PDF gate."));
                        return null;
                    }

                    // The render ends the gate when PDFium returns, which may be after this call has
                    // given up on it.
                    await PdfRenderGate.Semaphore.WaitAsync(token).ConfigureAwait(false);
                    render = PdfRenderGate.Begin();
                    try
                    {
                        ImagingLog.Render(_logger, kind, operation, type, gated.Length);
                        started = _timeProvider.GetTimestamp();
                        PdfRenderGate.Render owned = render;
                        work = Task.Run(() => RenderPdf(gated, policy, owned, token), CancellationToken.None);
                    }
                    catch
                    {
                        PdfRenderGate.End(render);
                        throw;
                    }
                }
                else
                {
                    // Section: the image path, in an image slot, on the pool so the budget can end the wait.
                    await _imageSlots.WaitAsync(token).ConfigureAwait(false);
                    ImagingLog.Render(_logger, kind, operation, type, gated.Length);
                    started = _timeProvider.GetTimestamp();
                    work = RunInSlot(() => SkiaPipeline.Thumbnail(gated, type, policy, ImagePolicy.DefaultMaxInputPixels, token));
                }

                byte[] thumbnail = await work.WaitAsync(token).ConfigureAwait(false);
                RecordDuration(kind, operation, started);
                return thumbnail;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
            {
                // Section: the budget ran out, waiting for a gate or rendering. A PDF render still
                // inside PDFium is marked abandoned, so later thumbnails do not queue behind it.
                if (render is not null && work is { IsCompleted: false })
                {
                    PdfRenderGate.Abandon(render);
                }

                RecordDuration(kind, operation, started);
                RecordFailure(kind, operation, new ImageFailure(BlobTelemetryTagValues.ImageTimeoutReason, "The render did not finish within its time budget."));
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
                ImagingLog.UnexpectedFailure(_logger, exception, kind, operation, type);
                return null;
            }
            finally
            {
                // Section: once this call returns, the caller's stream is never touched again:
                // closing the view waits for a read in flight and refuses every later one.
                gated.Dispose();
                Observe(work);
            }
        }

        /// <summary>
        ///     Runs image work on the pool in a slot the caller already holds, releasing the slot when
        ///     the work ends, however it ends.
        /// </summary>
        /// <typeparam name="T">The work's result.</typeparam>
        /// <param name="work">The work.</param>
        /// <returns>The running work.</returns>
        private Task<T> RunInSlot<T>(Func<T> work)
        {
            try
            {
                return Task.Run(
                    () =>
                    {
                        try
                        {
                            return work();
                        }
                        finally
                        {
                            _imageSlots.Release();
                        }
                    },
                    CancellationToken.None);
            }
            catch
            {
                _imageSlots.Release();
                throw;
            }
        }

        /// <summary>
        ///     Reads a PDF, walks page 1 against the caps, renders it and encodes the thumbnail; the
        ///     PDF gate ends as soon as PDFium is done.
        /// </summary>
        /// <param name="input">The gated PDF, at most the input cap.</param>
        /// <param name="policy">The thumbnail policy.</param>
        /// <param name="render">The render holding the gate.</param>
        /// <param name="token">The call's budget and cancellation.</param>
        /// <returns>The encoded thumbnail.</returns>
        private byte[] RenderPdf(GatedReadStream input, ThumbnailPolicy policy, PdfRenderGate.Render render, CancellationToken token)
        {
            SKBitmap bitmap;
            try
            {
                // A budget that ran out while this task was being scheduled ends it before PDFium.
                token.ThrowIfCancellationRequested();
                byte[] pdf = ReadAll(input, token);
                PdfPreflight.Run(pdf, token);

                // The render reads its own copy through a view the budget still closes, so PDFium's
                // lazy reads stop when the budget runs out.
                using var memory = new MemoryStream(pdf, writable: false);
                using var view = new GatedReadStream(memory, token);
                bitmap = _pdf.RenderFirstPage(view, policy.Size);
            }
            finally
            {
                PdfRenderGate.End(render);
            }

            using (bitmap)
            {
                token.ThrowIfCancellationRequested();
                return SkiaPipeline.EncodeBitmap(bitmap, policy.Format);
            }
        }

        /// <summary>Reads the whole gated input into memory.</summary>
        /// <param name="input">The input, at most the PDF input cap long.</param>
        /// <param name="token">Reported when the gate closed mid-read.</param>
        /// <returns>The bytes.</returns>
        private static byte[] ReadAll(GatedReadStream input, CancellationToken token)
        {
            input.Position = 0;
            byte[] bytes = new byte[input.Length];
            int length = 0;
            while (length < bytes.Length)
            {
                int read = input.Read(bytes.AsSpan(length));
                if (read == 0)
                {
                    token.ThrowIfCancellationRequested();
                    throw ImageFailure.DecodeFailed("The PDF could not be read to its end.");
                }

                length += read;
            }

            return bytes;
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

        /// <summary>Observes work this call gave up on, so a late failure is never an unobserved task.</summary>
        /// <param name="work">The work, or null.</param>
        private static void Observe(Task? work)
        {
            if (work is { IsCompleted: false })
            {
                _ = work.ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        /// <summary>A budget on the processor's clock; one that never fires for an infinite timeout.</summary>
        /// <param name="timeout">The timeout.</param>
        /// <returns>The source; the caller disposes it.</returns>
        private CancellationTokenSource Budget(TimeSpan timeout)
        {
            return timeout == Timeout.InfiniteTimeSpan ? new CancellationTokenSource() : new CancellationTokenSource(timeout, _timeProvider);
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

        /// <summary>Checks a time budget.</summary>
        /// <param name="timeout">The budget.</param>
        private static void ValidateTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The time budget is positive or infinite.");
            }
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
