// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.PdfWorker;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     The platform's image processor, on SkiaSharp, with PDF first-page thumbnails rendered by
    ///     PDFium in a disposable worker process.
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
    ///         cap, or a PDF's first page. PDFium never runs in this process: each PDF thumbnail is
    ///         rendered by a new worker process, which walks page 1 against the caps, renders it and
    ///         answers with a PNG, under operating-system memory, CPU and access limits, killed when
    ///         the budget runs out. A hostile document is contained in its worker as far as those
    ///         limits reach (the package README lists what they do not cover); a worker that fails in
    ///         any way means no thumbnail, never a failed upload. PDF thumbnails render where those
    ///         limits hold: on Linux with glibc, and, for developers only, on Windows and macOS in
    ///         the Development environment. Any other host renders none, and starts no worker: a
    ///         PDF there has no thumbnail, as a file of any other type has none.
    ///     </para>
    ///     <para>
    ///         Native work cannot be stopped mid-call, so it runs behind slots released only when the
    ///         native work ends: <see cref="SkiaImageProcessorOptions.ImageConcurrency" /> slots for
    ///         images, and <see cref="SkiaImageProcessorOptions.PdfWorkerConcurrency" /> slots for PDF
    ///         workers, whose work ends with their process. A call's
    ///         budget bounds its wait and its work; when it runs out the call returns at once, the
    ///         input is closed to the native reader, so a decode still in flight stops at its next
    ///         read, and a PDF worker is killed.
    ///     </para>
    ///     <para>
    ///         The processor records <c>tellma.blobs.image.duration</c> and
    ///         <c>tellma.blobs.image.failures</c>, and writes the <c>BlobImageRender</c> debug event
    ///         immediately before native code first touches an input. Disposing it kills the PDF
    ///         workers still running.
    ///     </para>
    /// </remarks>
    public sealed class SkiaImageProcessor : IImageProcessor, IDisposable
    {
        /// <summary>The code of every <see cref="ImageRejectedException" /> this processor throws.</summary>
        public const string RejectedCode = "Image.Rejected";

        /// <summary>The argument of a pixel-cap refusal that carries the declared width.</summary>
        public const string WidthArgument = "width";

        /// <summary>The argument of a pixel-cap refusal that carries the declared height.</summary>
        public const string HeightArgument = "height";

        /// <summary>The argument of a pixel-cap refusal that carries the cap.</summary>
        public const string MaxPixelsArgument = "maxPixels";

        // A worker's CPU limit beyond the call's budget: the wall clock kills it at the budget, and
        // the CPU limit bounds it on its own should the host be gone.
        private static readonly TimeSpan CpuAllowance = TimeSpan.FromSeconds(1);

        // The most CPU time any PDF worker may use, a long or infinite budget included.
        private static readonly TimeSpan CpuCeiling = TimeSpan.FromSeconds(60);

        private readonly ILogger _logger;
        private readonly ImagingMetrics _metrics;
        private readonly TimeProvider _timeProvider;
        private readonly Func<byte[], Stream> _openInput;
        private readonly int _concurrency;

        // The image-work slots. Never disposed: work abandoned at shutdown still releases its slot
        // when it ends, and the semaphore holds no handle unless one is asked for.
        private readonly SemaphoreSlim _imageSlots;

        // The PDF worker slots, apart so that PDFs never hold the slots uploads need; never disposed,
        // for the same reason as the image slots.
        private readonly SemaphoreSlim _pdfSlots;

        /// <summary>Creates the processor.</summary>
        /// <param name="logger">The processor's logger.</param>
        /// <param name="meterFactory">The host's meter factory.</param>
        /// <param name="timeProvider">The clock the time budgets run on.</param>
        /// <param name="options">The processor's options.</param>
        /// <param name="environment">
        ///     The host's environment, which decides whether a host on Windows or macOS renders PDF
        ///     thumbnails: only in Development. A host that passes none is not in Development.
        /// </param>
        /// <exception cref="ArgumentNullException">An argument other than <paramref name="environment" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A concurrency is not positive, or the worker memory limit is below its minimum.</exception>
        public SkiaImageProcessor(
            ILogger<SkiaImageProcessor> logger,
            IMeterFactory meterFactory,
            TimeProvider timeProvider,
            IOptions<SkiaImageProcessorOptions> options,
            IHostEnvironment? environment = null)
            : this(
                logger,
                meterFactory,
                timeProvider,
                (options ?? throw new ArgumentNullException(nameof(options))).Value,
                pdfThumbnails: PdfThumbnailPolicy.For(environment))
        {
        }

        /// <summary>Creates the processor over given seams.</summary>
        /// <param name="logger">The processor's logger.</param>
        /// <param name="meterFactory">The host's meter factory.</param>
        /// <param name="timeProvider">The clock the time budgets run on.</param>
        /// <param name="options">The processor's options.</param>
        /// <param name="pdf">Renders PDF pages in workers; the PDF worker under the options' memory limit when null.</param>
        /// <param name="openInput">Opens an upload's bytes for the decoder; an in-memory stream when null.</param>
        /// <param name="pdfThumbnails">Whether PDF thumbnails render at all.</param>
        internal SkiaImageProcessor(
            ILogger logger,
            IMeterFactory meterFactory,
            TimeProvider timeProvider,
            SkiaImageProcessorOptions options,
            PdfWorkerClient? pdf = null,
            Func<byte[], Stream>? openInput = null,
            PdfThumbnailSupport pdfThumbnails = PdfThumbnailSupport.Enabled)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(meterFactory);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ImageConcurrency, nameof(options));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PdfWorkerConcurrency, nameof(options));
            ArgumentOutOfRangeException.ThrowIfLessThan(options.PdfWorkerMemoryLimit, SkiaImageProcessorOptions.MinimumPdfWorkerMemoryLimit, nameof(options));

            _logger = logger;
            _metrics = new ImagingMetrics(meterFactory);
            _timeProvider = timeProvider;
            PdfWorkers = pdf ?? new PdfWorkerClient(new PdfWorkerLauncher(), options.PdfWorkerMemoryLimit);
            PdfThumbnails = pdfThumbnails;
            _openInput = openInput ?? (static bytes => new MemoryStream(bytes, writable: false));
            _concurrency = options.ImageConcurrency;
            _imageSlots = new SemaphoreSlim(_concurrency, _concurrency);
            _pdfSlots = new SemaphoreSlim(options.PdfWorkerConcurrency, options.PdfWorkerConcurrency);
        }

        /// <summary>How many image slots are free now; for the suite.</summary>
        internal int FreeImageSlots => _imageSlots.CurrentCount;

        /// <summary>How many PDF worker slots are free now; for the suite.</summary>
        internal int FreePdfSlots => _pdfSlots.CurrentCount;

        /// <summary>The PDF workers.</summary>
        internal PdfWorkerClient PdfWorkers { get; }

        /// <summary>Whether this host renders PDF thumbnails at all.</summary>
        internal PdfThumbnailSupport PdfThumbnails { get; }

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

            // Section: the work, on the pool under its own budget. Once the work is running, it
            // releases the slot itself when its native part ends, which may be after this call has
            // given up on it; until then, the finally below releases it.
            using CancellationTokenSource workBudget = Budget(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, workBudget.Token);
            CancellationToken token = linked.Token;
            Stream? bytes = null;
            GatedReadStream? gated = null;
            Task<ProcessedImage>? work = null;
            long? started = null;
            try
            {
                bytes = _openInput(input);
                GatedReadStream view = gated = new GatedReadStream(bytes, token);
                ImagingLog.Render(_logger, kind, operation, contentType, input.LongLength);
                started = _timeProvider.GetTimestamp();
                work = RunInSlot(() => SkiaPipeline.Process(view, contentType, policy, thumbnail, token));
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
                // Section: a slot the work never took over goes back here. From here on the input
                // is never touched: closing the view waits for a read in flight and refuses every
                // later one.
                if (work is null)
                {
                    _imageSlots.Release();
                }

                gated?.Dispose();
                if (bytes is not null)
                {
                    await bytes.DisposeAsync().ConfigureAwait(false);
                }

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

            // Section: a host that renders no PDF thumbnails starts no worker for one. That is the
            // host's shape, not the document's fault, so it counts as no failure either.
            if (isPdf && PdfThumbnails != PdfThumbnailSupport.Enabled)
            {
                return null;
            }

            const string operation = BlobTelemetryTagValues.ImageThumbnail;

            // Section: the time budget, covering the wait for a slot and the render. It closes the
            // input to the native reader as the caller's token does, and kills a PDF worker; the
            // caller's token alone surfaces as a cancellation.
            using CancellationTokenSource budget = Budget(timeout);
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

                // Section: a PDF over the worker's input cap is not read at all, and a thumbnail
                // larger than the worker renders is not asked of it.
                if (isPdf && gated.Length > WorkerProtocol.MaxInputBytes)
                {
                    RecordFailure(kind, operation, ImageFailure.DecodeFailed($"The PDF is larger than {WorkerProtocol.MaxInputBytes} bytes."));
                    return null;
                }

                if (isPdf && policy.Size > WorkerProtocol.MaxThumbnailSize)
                {
                    RecordFailure(kind, operation, ImageFailure.DecodeFailed($"PDF thumbnails are at most {WorkerProtocol.MaxThumbnailSize} pixels on a side."));
                    return null;
                }

                // Section: a slot, on the pool so the budget can end the wait: a PDF worker slot for a
                // PDF, so PDFs never hold the slots image uploads need, and an image slot otherwise.
                // Until the work has taken the slot over, a failure gives it back here.
                SemaphoreSlim slots = isPdf ? _pdfSlots : _imageSlots;
                await slots.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    ImagingLog.Render(_logger, kind, operation, type, gated.Length);
                    started = _timeProvider.GetTimestamp();
                    work = isPdf
                        ? RunPdfInSlot(gated, policy, CpuLimit(timeout), token)
                        : RunInSlot(() => SkiaPipeline.Thumbnail(gated, type, policy, ImagePolicy.DefaultMaxInputPixels, token));
                }
                catch
                {
                    _ = slots.Release();
                    throw;
                }

                byte[] thumbnail = await work.WaitAsync(token).ConfigureAwait(false);
                RecordDuration(kind, operation, started);
                return thumbnail;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
            {
                // Section: the budget ran out, waiting for a slot or rendering. A PDF worker is killed
                // by the same token; its slot comes back once its process is gone.
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
            catch (PdfWorkerException exception)
            {
                // Section: the worker failed rather than refusing the document: worth a warning, for
                // it may be a hostile upload stopped by a limit, or a PDFium bug.
                RecordDuration(kind, operation, started);
                _metrics.RecordFailure(kind, operation, BlobTelemetryTagValues.ImageDecodeFailedReason);
                ImagingLog.PdfWorkerFailed(_logger, exception, kind, exception.Failure.ToString(), exception.ExitCode, exception.Message);
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
        ///     the work ends, however it ends. Should scheduling itself throw, the slot stays the
        ///     caller's to release.
        /// </summary>
        /// <typeparam name="T">The work's result.</typeparam>
        /// <param name="work">The work.</param>
        /// <returns>The running work.</returns>
        private Task<T> RunInSlot<T>(Func<T> work)
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

        /// <summary>
        ///     Renders a PDF thumbnail in a slot the caller already holds, on a thread of its own,
        ///     since the worker conversation blocks: reads the document, has a worker render page 1,
        ///     and encodes the checked page. The slot comes back once the worker's process is gone,
        ///     however the work ends.
        /// </summary>
        /// <param name="input">The gated PDF, at most the worker's input cap.</param>
        /// <param name="policy">The thumbnail policy.</param>
        /// <param name="cpuTime">The worker's CPU limit.</param>
        /// <param name="token">The call's budget and cancellation, which kill the worker.</param>
        /// <returns>The running work.</returns>
        private Task<byte[]> RunPdfInSlot(GatedReadStream input, ThumbnailPolicy policy, TimeSpan cpuTime, CancellationToken token)
        {
            // The thread is the worker's: it starts the worker, which on Linux the kernel kills when
            // that thread ends, and it ends with this work. The caller's continuations run on the
            // pool instead, so nothing the caller does next is tied to this thread's end.
            return Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        // A budget that ran out while this task was being scheduled ends it before a
                        // worker starts.
                        token.ThrowIfCancellationRequested();
                        byte[] pdf = ReadAll(input, token);
                        RenderedPage page = PdfWorkers.Render(pdf, policy.Size, cpuTime, token);
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            return SkiaPipeline.EncodeRenderedPage(page.Png, page.Width, page.Height, policy.Format);
                        }
                        catch (ImageFailure failure)
                        {
                            // The PNG passed its check yet did not decode: the worker is at fault.
                            throw new PdfWorkerException(PdfWorkerFailure.Protocol, $"The worker's page did not decode: {failure.Message}", failure);
                        }
                    }
                    finally
                    {
                        _pdfSlots.Release();
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        ///     A PDF worker's CPU limit for a call's budget: the budget and an allowance, never more
        ///     than the ceiling, and the ceiling for no budget, so that a worker always ends on its
        ///     own.
        /// </summary>
        /// <param name="timeout">The call's budget.</param>
        /// <returns>The limit.</returns>
        internal static TimeSpan CpuLimit(TimeSpan timeout)
        {
            return timeout == Timeout.InfiniteTimeSpan || timeout + CpuAllowance > CpuCeiling ? CpuCeiling : timeout + CpuAllowance;
        }

        /// <summary>Kills the PDF workers still running; their slots come back as they exit.</summary>
        public void Dispose()
        {
            PdfWorkers.Dispose();
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
