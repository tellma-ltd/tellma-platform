// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     A row of <c>core.Notifications</c>: one notification in one user's inbox. System-written:
    ///     only <see cref="INotifier" />, the inbox's own verbs and retention write it, never a save,
    ///     and its stack serves reads alone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every member is server-owned. A row stores its type's key and its arguments, never
    ///         text: a reader renders it in their own language through
    ///         <see cref="INotificationRenderer" /> or the client's string pack.
    ///     </para>
    ///     <para>
    ///         The recipient and the actor reference users; a query reaches them through the
    ///         <c>User</c> and <c>ActorUser</c> navigations their keys derive. The recipient and the
    ///         duplicate key are left out of the default projection and stay selectable.
    ///     </para>
    /// </remarks>
    [Table("Notifications", Schema = "core")]
    [TableType]
    [Stack(Operations = StackOperations.Query | StackOperations.Details)]
    [DefaultSelect("Id,Type,Severity,ArgumentsJson,TargetResource,TargetId,ActorUserId,CreatedAt,ReadAt")]
    public sealed class Notification : Entity<int>
    {
        /// <summary>The longest type key, <c>nvarchar(64)</c>: the handler-key grammar's bound.</summary>
        public const int TypeLength = 64;

        /// <summary>The width of the severity, <c>varchar(8)</c>: the platform's shortest enum column, which every member's name fits.</summary>
        public const int SeverityLength = 8;

        /// <summary>The longest serialised arguments, <c>nvarchar(4000)</c>.</summary>
        public const int ArgumentsLength = 4000;

        /// <summary>The longest target resource id, <c>varchar(128)</c>.</summary>
        public const int TargetResourceLength = 128;

        /// <summary>The longest duplicate key, <c>nvarchar(128)</c>.</summary>
        public const int DedupKeyLength = 128;

        /// <summary>The recipient, a user's id.</summary>
        [ServerOwned]
        public int UserId { get; set; }

        /// <summary>The key of the notification's registered type, <c>nvarchar(64)</c>.</summary>
        [ServerOwned]
        public string Type { get; set; } = null!;

        /// <summary>The row's severity: the raising request's, else its type's.</summary>
        [ServerOwned]
        public NotificationSeverity Severity { get; set; }

        /// <summary>
        ///     The message's arguments as a JSON object, <c>nvarchar(4000)</c>; null for none. Also the
        ///     data the client acts on when the row is opened.
        /// </summary>
        [ServerOwned]
        public string? ArgumentsJson { get; set; }

        /// <summary>The securable resource the notification points at, <c>varchar(128)</c>, such as <c>core.Export</c>; null for none.</summary>
        [ServerOwned]
        public string? TargetResource { get; set; }

        /// <summary>The id of the row the notification points at; null when it points at none.</summary>
        [ServerOwned]
        public long? TargetId { get; set; }

        /// <summary>The user whose action caused the notification; null when the system caused it.</summary>
        [ServerOwned]
        public int? ActorUserId { get; set; }

        /// <summary>
        ///     The key that collapses repeated notifications of one subject for one recipient,
        ///     <c>nvarchar(128)</c>; null for a notification that never collapses.
        /// </summary>
        [ServerOwned]
        public string? DedupKey { get; set; }

        /// <summary>When the notification was raised, or last refreshed in place, <c>datetimeoffset(3)</c>.</summary>
        [ServerOwned]
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>When the recipient read it, <c>datetimeoffset(3)</c>; null while unread.</summary>
        [ServerOwned]
        public DateTimeOffset? ReadAt { get; set; }
    }
}
