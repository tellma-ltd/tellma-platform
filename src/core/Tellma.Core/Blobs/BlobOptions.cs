// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The settings of blob staging, the sweep and the upload limits, bound from
    ///     <c>Tellma:Blobs</c> and validated by the startup gate: every value positive, and
    ///     <see cref="MaxUploadSize" /> at least the largest kind's size limit.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same section carries the image processor's keys, which the processor binds itself
    ///         and these options leave alone: <c>ImageConcurrency</c> (the most image operations at
    ///         once, PDF thumbnails included; half the processor count by default, at least one),
    ///         <c>PdfWorkerConcurrency</c> (the most PDF thumbnail workers at once) and
    ///         <c>PdfWorkerMemoryLimit</c> (the memory cap of one PDF thumbnail worker, 256 MiB by
    ///         default). The file-system store binds <c>Tellma:Blobs:FileSystem</c>.
    ///     </para>
    ///     <para>
    ///         Spans that a statement binds in seconds (<see cref="StagingTtl" />,
    ///         <see cref="SweepReclaimAfter" />) are held to what a 32-bit second count carries.
    ///     </para>
    /// </remarks>
    public sealed class BlobOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Blobs";

        /// <summary>How long a staged blob waits for a save to attach it before the sweep reclaims it; 24 hours by default.</summary>
        public TimeSpan StagingTtl { get; set; } = TimeSpan.FromHours(24);

        /// <summary>The most rows one sweep batch claims; 500 by default.</summary>
        public int SweepBatchSize { get; set; } = 500;

        /// <summary>How long a claimed row may stay claimed before another sweep claims it again; 60 minutes by default.</summary>
        public TimeSpan SweepReclaimAfter { get; set; } = TimeSpan.FromMinutes(60);

        /// <summary>The most unexpired staged blobs one uploader may hold; 200 by default. The platform's own scope is exempt.</summary>
        public int MaxStagedPerUser { get; set; } = 200;

        /// <summary>The most bytes of unexpired staged blobs one uploader may hold; 512 MiB by default. The platform's own scope is exempt.</summary>
        public long MaxStagedBytesPerUser { get; set; } = 512L * 1024 * 1024;

        /// <summary>
        ///     The largest upload the host accepts, whatever the kind; 100 MiB by default. The upload
        ///     endpoint raises the server's request-body limit to it, and a kind whose size limit
        ///     exceeds it fails startup rather than being silently capped.
        /// </summary>
        public long MaxUploadSize { get; set; } = 100L * 1024 * 1024;

        /// <summary>
        ///     How long rendering a thumbnail of a file that is not an image kind may take, waiting for
        ///     a render slot included; 5 seconds by default. A render past it stores no thumbnail.
        /// </summary>
        public TimeSpan ThumbnailTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        ///     How long normalising an uploaded image may wait for a processing slot, and then how long
        ///     the work may take; 15 seconds by default. No slot in time is a busy server, which the
        ///     client may retry; work past it refuses the image.
        /// </summary>
        public TimeSpan ImageProcessTimeout { get; set; } = TimeSpan.FromSeconds(15);
    }
}
