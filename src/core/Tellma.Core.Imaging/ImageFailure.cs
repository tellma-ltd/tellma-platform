// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     Why the imaging pipeline could not turn an input into an image: thrown inside the pipeline
    ///     and translated at the processor's boundary, into an <see cref="ImageRejectedException" />
    ///     for an upload, or into a null thumbnail.
    /// </summary>
    /// <param name="reason">One of the reason values of <see cref="ImageRejectedException" />.</param>
    /// <param name="message">What failed, for the debug log.</param>
    /// <param name="innerException">The library's exception, when one caused the failure.</param>
    internal sealed class ImageFailure(string reason, string message, Exception? innerException = null)
        : Exception(message, innerException)
    {
        /// <summary>
        ///     The reason, as the failure counter and the rejection carry it:
        ///     <see cref="ImageRejectedException.FormatReason" />,
        ///     <see cref="ImageRejectedException.TooManyPixelsReason" /> or
        ///     <see cref="ImageRejectedException.DecodeFailedReason" />.
        /// </summary>
        public string Reason { get; } = reason;

        /// <summary>The declared width, when the failure is a pixel-cap refusal.</summary>
        public int? Width { get; init; }

        /// <summary>The declared height, when the failure is a pixel-cap refusal.</summary>
        public int? Height { get; init; }

        /// <summary>The pixel cap the input exceeded, when the failure is a pixel-cap refusal.</summary>
        public long? MaxPixels { get; init; }

        /// <summary>The input is not one of the accepted formats.</summary>
        /// <param name="message">What was found.</param>
        /// <returns>The failure.</returns>
        public static ImageFailure Format(string message)
        {
            return new ImageFailure(ImageRejectedException.FormatReason, message);
        }

        /// <summary>The input could not be decoded.</summary>
        /// <param name="message">What failed.</param>
        /// <param name="innerException">The library's exception, if any.</param>
        /// <returns>The failure.</returns>
        public static ImageFailure DecodeFailed(string message, Exception? innerException = null)
        {
            return new ImageFailure(ImageRejectedException.DecodeFailedReason, message, innerException);
        }

        /// <summary>The input's header declares more pixels than the cap.</summary>
        /// <param name="width">The declared width.</param>
        /// <param name="height">The declared height.</param>
        /// <param name="maxPixels">The cap.</param>
        /// <returns>The failure.</returns>
        public static ImageFailure TooManyPixels(int width, int height, long maxPixels)
        {
            return new ImageFailure(
                ImageRejectedException.TooManyPixelsReason,
                $"The image declares {width} x {height} pixels, more than the cap of {maxPixels}.")
            {
                Width = width,
                Height = height,
                MaxPixels = maxPixels,
            };
        }
    }
}
