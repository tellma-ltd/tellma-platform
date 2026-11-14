// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     The processor's own settings, bound from the blob section of configuration
    ///     (<c>Tellma:Blobs:ImageConcurrency</c>, <c>Tellma:Blobs:PdfWorkerConcurrency</c>,
    ///     <c>Tellma:Blobs:PdfWorkerMemoryLimit</c>), beside the per-call time budgets the blob
    ///     service passes in.
    /// </summary>
    public sealed class SkiaImageProcessorOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionName = "Tellma:Blobs";

        /// <summary>The default <see cref="PdfWorkerMemoryLimit" />: 256 MiB.</summary>
        public const long DefaultPdfWorkerMemoryLimit = 256L * 1024 * 1024;

        /// <summary>The smallest <see cref="PdfWorkerMemoryLimit" /> accepted: 32 MiB, below which no page renders.</summary>
        public const long MinimumPdfWorkerMemoryLimit = 32L * 1024 * 1024;

        /// <summary>
        ///     How many image decodes, normalisations and image thumbnails may run at once in the
        ///     process. Each holds up to one decoded frame under the pixel cap, so this bounds the
        ///     processor's memory and cores for images; a slot is released only when its native work
        ///     ends. PDF thumbnails never take these slots. Default: half the processor count, at
        ///     least one.
        /// </summary>
        public int ImageConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

        /// <summary>
        ///     How many PDF workers may run at once in the process, apart from
        ///     <see cref="ImageConcurrency" />, so that image uploads never wait behind PDF thumbnails.
        ///     Each worker takes up to <see cref="PdfWorkerMemoryLimit" /> beyond its runtime, and its
        ///     slot is released only when its process is gone; a PDF that finds every slot taken waits
        ///     for one within its own time budget, and gets no thumbnail when the budget runs out.
        ///     Default: a quarter of the processor count, at least one.
        /// </summary>
        public int PdfWorkerConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount / 4);

        /// <summary>
        ///     The memory, in bytes, a PDF worker may take beyond what its .NET runtime holds once
        ///     started, enforced by the operating system: a Job Object's committed-memory limit on
        ///     Windows, <c>RLIMIT_DATA</c> on Linux, where it also caps the worker's managed heap and
        ///     sizes its address-space limit. A document that needs more is stopped and gets no
        ///     thumbnail. Honest pages take a few tens of megabytes. Default: 256 MiB; at least 32 MiB.
        /// </summary>
        public long PdfWorkerMemoryLimit { get; set; } = DefaultPdfWorkerMemoryLimit;
    }

    /// <summary>Refuses a non-positive concurrency or too small a worker memory limit when the options are first resolved.</summary>
    internal sealed class SkiaImageProcessorOptionsValidator : IValidateOptions<SkiaImageProcessorOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, SkiaImageProcessorOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            // Section: one failure per invalid setting, each naming its key.
            List<string> failures = [];
            if (options.ImageConcurrency <= 0)
            {
                failures.Add($"{SkiaImageProcessorOptions.SectionName}:{nameof(SkiaImageProcessorOptions.ImageConcurrency)} must be positive; it is {options.ImageConcurrency}.");
            }

            if (options.PdfWorkerConcurrency <= 0)
            {
                failures.Add($"{SkiaImageProcessorOptions.SectionName}:{nameof(SkiaImageProcessorOptions.PdfWorkerConcurrency)} must be positive; it is {options.PdfWorkerConcurrency}.");
            }

            if (options.PdfWorkerMemoryLimit < SkiaImageProcessorOptions.MinimumPdfWorkerMemoryLimit)
            {
                failures.Add($"{SkiaImageProcessorOptions.SectionName}:{nameof(SkiaImageProcessorOptions.PdfWorkerMemoryLimit)} must be at least {SkiaImageProcessorOptions.MinimumPdfWorkerMemoryLimit} bytes; it is {options.PdfWorkerMemoryLimit}.");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
