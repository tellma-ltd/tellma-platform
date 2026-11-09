// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     The processor's own setting, bound from the blob section of configuration
    ///     (<c>Tellma:Blobs:ImageConcurrency</c>), beside the per-call time budgets the blob service
    ///     passes in.
    /// </summary>
    public sealed class SkiaImageProcessorOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionName = "Tellma:Blobs";

        /// <summary>
        ///     How many image decodes, normalisations and image thumbnails may run at once in the
        ///     process. Each holds up to one decoded frame under the pixel cap, so this bounds the
        ///     processor's memory and cores; a slot is released only when its native work ends.
        ///     Default: half the processor count, at least one.
        /// </summary>
        public int ImageConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    }

    /// <summary>Refuses a non-positive concurrency when the options are first resolved.</summary>
    internal sealed class SkiaImageProcessorOptionsValidator : IValidateOptions<SkiaImageProcessorOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, SkiaImageProcessorOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.ImageConcurrency > 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail($"{SkiaImageProcessorOptions.SectionName}:{nameof(SkiaImageProcessorOptions.ImageConcurrency)} must be positive; it is {options.ImageConcurrency}.");
        }
    }
}
