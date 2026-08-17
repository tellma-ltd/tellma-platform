// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     The brand mark that rides along inside every HTML email, as a part of the message rather
    ///     than something the reader's client has to go and fetch.
    /// </summary>
    /// <remarks>
    ///     Three constraints leave no other option. Mail clients drop SVG, so the vector wordmark the
    ///     UI uses cannot travel; a remote image is blocked until the reader trusts the sender, which
    ///     on a first invitation is exactly never; and an on-premise authority has no address a
    ///     recipient's client could reach anyway. An inline part is carried by the message, so it
    ///     renders on the first read, offline, and behind a firewall.
    /// </remarks>
    internal static class EmailWordmark
    {
        /// <summary>The content id the HTML body references the image by.</summary>
        internal const string ContentId = "tellma-wordmark";

        /// <summary>The file name the part carries.</summary>
        internal const string FileName = "wordmark.png";

        /// <summary>The part's media type.</summary>
        internal const string ContentType = "image/png";

        /// <summary>The width the mark is drawn at, in CSS pixels.</summary>
        internal const int DisplayWidth = 132;

        /// <summary>The height the mark is drawn at, in CSS pixels.</summary>
        internal const int DisplayHeight = 37;

        /// <summary>The manifest name the build gives the embedded image.</summary>
        private const string ResourceName = "Tellma.Identity.Services.Email.tellma-wordmark-email.png";

        // Read once: the bytes are immutable and every message sent asks for the same ones.
        private static readonly Lazy<ReadOnlyMemory<byte>> Content = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Builds the inline attachment carrying the mark.</summary>
        /// <returns>The attachment, with the content id the body references.</returns>
        internal static EmailAttachment Attachment()
        {
            return new EmailAttachment(FileName, ContentType, Content.Value, ContentId);
        }

        /// <summary>Reads the embedded image into memory.</summary>
        private static ReadOnlyMemory<byte> Load()
        {
            Assembly assembly = typeof(EmailWordmark).Assembly;

            // A build that dropped the resource would otherwise fail far away, as mail with a
            // broken image, so name what was looked for and what is actually there.
            using Stream stream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"The email wordmark '{ResourceName}' is not embedded in {assembly.GetName().Name}. "
                    + $"Embedded resources: {string.Join(", ", assembly.GetManifestResourceNames())}.");

            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
