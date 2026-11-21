// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     A row of <c>core.Blobs</c>: one stored file, referenced by at most one owner row through a
    ///     <see cref="BlobReferenceAttribute" /> column. System-written: never saved through a stack, and
    ///     immutable except for its state and the two timestamps that follow it.
    /// </summary>
    /// <remarks>
    ///     The object's storage name and content hash are columns of the table the model maps but the
    ///     class does not carry, so neither ever reaches a client or a query. Reached only through the
    ///     navigation each owner column defines.
    /// </remarks>
    [Table("Blobs", Schema = "core")]
    [RelatedSelect("Id,Kind,ContentType,FileName,Size,Width,Height,Variants")]
    public sealed class Blob : Entity<int>
    {
        /// <summary>The blob's kind, the one the owner column declares, <c>nvarchar(40)</c>.</summary>
        public string Kind { get; set; } = null!;

        /// <summary>Where the blob is in its life.</summary>
        public BlobState State { get; set; }

        /// <summary>The server-determined media type of the stored file, <c>nvarchar(100)</c>.</summary>
        public string ContentType { get; set; } = null!;

        /// <summary>
        ///     The sanitised original file name, <c>nvarchar(255)</c>, for display and downloads only and
        ///     never a path; null when the upload named none.
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>The size of the stored file in bytes.</summary>
        public long Size { get; set; }

        /// <summary>The width of an image in pixels; null for any other file.</summary>
        public int? Width { get; set; }

        /// <summary>The height of an image in pixels; null for any other file.</summary>
        public int? Height { get; set; }

        /// <summary>
        ///     The comma-separated names of the variants stored beside the file, such as <c>thumb</c>,
        ///     <c>nvarchar(100)</c>; empty for none.
        /// </summary>
        public string Variants { get; set; } = null!;

        /// <summary>When the blob was staged, <c>datetimeoffset(3)</c>; server-owned.</summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>The user who uploaded the blob, the only user who may attach it; server-owned.</summary>
        public int CreatedById { get; set; }

        /// <summary>When the blob was attached, <c>datetimeoffset(3)</c>; null until then.</summary>
        public DateTimeOffset? CommittedAt { get; set; }

        /// <summary>
        ///     The staging deadline while staged, the release time while released, and the reclaim time
        ///     while being deleted, <c>datetimeoffset(3)</c>; null while committed.
        /// </summary>
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
