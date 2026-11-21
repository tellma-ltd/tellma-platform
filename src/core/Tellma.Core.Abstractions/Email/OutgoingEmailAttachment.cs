// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     A row of <c>core.OutgoingEmailAttachments</c>: one file an outgoing email carries, held as a
    ///     blob of the <c>email-attachment</c> kind that the email owns alone. The file's content type
    ///     and size come from its blob row, and a download authorises through the email's read grant.
    /// </summary>
    [Table("OutgoingEmailAttachments", Schema = "core")]
    [TableType]
    public sealed class OutgoingEmailAttachment : ChildEntity
    {
        /// <summary>The blob kind of every email attachment.</summary>
        public const string BlobKind = "email-attachment";

        /// <summary>The width of <see cref="FileName" /> and <see cref="ContentId" />, <c>nvarchar(255)</c>.</summary>
        public const int NameLength = 255;

        /// <summary>The email the file is attached to.</summary>
        [ParentKey]
        public int OutgoingEmailId { get; set; }

        /// <summary>The file name the recipient sees, <c>nvarchar(255)</c>.</summary>
        [MaxLength(NameLength)]
        public string FileName { get; set; } = null!;

        /// <summary>The content id an HTML body references the file by as an inline <c>cid:</c> image, <c>nvarchar(255)</c>; null for an ordinary attachment.</summary>
        [MaxLength(NameLength)]
        public string? ContentId { get; set; }

        /// <summary>The file's blob, navigation <c>File</c>; the outbox stages it before the email is saved.</summary>
        [BlobReference(BlobKind, BlobPreset.Attachment, ReadAccess = BlobReadAccess.OwnerRead)]
        public int? FileId { get; set; }
    }
}
