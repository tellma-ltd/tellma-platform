// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A member of the tenant: a person, a service account, or the tenant's own system user (row 1).
    ///     Every top-level row's audit columns reference it. The platform's default: a distribution
    ///     extends it by inheritance and registers its class as the leaf.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="State" /> follows the tenant's own evidence only: a user is
    ///         <see cref="UserState.New" /> until invited, <see cref="UserState.Invited" /> until the
    ///         first sign-in to the tenant (or, for a service account, until credentials are issued), and
    ///         <see cref="UserState.Joined" /> after. The database computes it from
    ///         <see cref="InvitedAt" /> and <see cref="JoinedAt" />, and its checks keep
    ///         <see cref="Subject" /> and <see cref="Email" /> in step with the kind and the evidence.
    ///     </para>
    ///     <para>
    ///         <see cref="IsActive" /> is orthogonal to the state: a deactivated user is refused on
    ///         every request. Each write of the user's own row moves the user's preferences tag.
    ///     </para>
    /// </remarks>
    [Table("Users", Schema = "core")]
    [TableType]
    [Temporal]
    [BumpsUserVersionTag(UserVersionTagNames.Preferences, nameof(Id))]
    [ApiResource]
    [Stack(Operations = StackOperations.All | StackOperations.Enlist)]
    public class User : TopLevelEntity, IActivatable
    {
        /// <summary>
        ///     What the row stands for; write-once, <see cref="UserKind.Individual" /> or
        ///     <see cref="UserKind.Service" /> at creation. <see cref="UserKind.System" /> is row 1's
        ///     alone.
        /// </summary>
        [WriteOnce]
        public UserKind Kind { get; set; } = UserKind.Individual;

        /// <summary>
        ///     The identity server's subject of a person, or a service account's client id; server-owned,
        ///     <c>varchar(255)</c> compared byte for byte, unique when present. Null until the user is
        ///     invited or issued credentials, and always null for the system user.
        /// </summary>
        [ServerOwned]
        [Collation(DatabaseCollations.Binary)]
        [Column(TypeName = "varchar(255)")]
        public string? Subject { get; set; }

        /// <summary>
        ///     The sign-in email address, <c>nvarchar(255)</c>, trimmed and lower-cased; the natural key.
        ///     Required for an individual and refused for any other kind; editable only while the user
        ///     is <see cref="UserState.New" />.
        /// </summary>
        [MaxLength(255)]
        [NaturalKey]
        [Searchable]
        public string? Email { get; set; }

        /// <summary>
        ///     How far the user has come; computed and stored by the database from
        ///     <see cref="InvitedAt" /> and <see cref="JoinedAt" />, and never written by the application.
        /// </summary>
        public UserState State { get; set; }

        /// <summary>When the user was last invited, <c>datetimeoffset(3)</c>; server-owned.</summary>
        [ServerOwned]
        public DateTimeOffset? InvitedAt { get; set; }

        /// <summary>The identity server's answer to the last successful invitation; server-owned.</summary>
        [ServerOwned]
        public InviteStatus? InviteStatus { get; set; }

        /// <summary>
        ///     The identity server's error for the last failed invitation of the user, verbatim,
        ///     <c>nvarchar(1024)</c>; server-owned, and cleared by a successful one.
        /// </summary>
        [ServerOwned]
        [MaxLength(1024)]
        public string? LastInviteError { get; set; }

        /// <summary>
        ///     When an individual first signed in to the tenant, or a service account was first issued
        ///     credentials, <c>datetimeoffset(3)</c>; server-owned, the join evidence.
        /// </summary>
        [ServerOwned]
        public DateTimeOffset? JoinedAt { get; set; }

        /// <summary>The name in the tenant's primary language, <c>nvarchar(255)</c>.</summary>
        [MaxLength(255)]
        [Multilingual]
        [Searchable]
        public string Name { get; set; } = null!;

        /// <summary>The name in the tenant's secondary language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the tenant's tertiary language.</summary>
        public string? Name3 { get; set; }

        /// <summary>The user's picture, a blob any member may read.</summary>
        [BlobReference("user-image", BlobPreset.Avatar, ReadAccess = BlobReadAccess.AnyMember)]
        public int? ImageId { get; set; }

        /// <summary>
        ///     The user's handwritten signature, a transparent image of at most 1 MiB that any member
        ///     may read, so it can be shown over an approval on any document the member can read.
        /// </summary>
        [BlobReference("user-signature", BlobPreset.Signature, MaxSizeBytes = 1_048_576, ReadAccess = BlobReadAccess.AnyMember)]
        public int? SignatureId { get; set; }

        /// <summary>A bare language code, such as <c>ar</c>, <c>varchar(35)</c>; null for the tenant's primary language.</summary>
        [ValueSet(CoreValueSets.Language)]
        [Column(TypeName = "varchar(35)")]
        public string? PreferredLanguage { get; set; }

        /// <summary>A calendar code, such as <c>gc</c>, <c>varchar(16)</c>; null for the tenant's primary calendar.</summary>
        [ValueSet(CoreValueSets.Calendar)]
        [Column(TypeName = "varchar(16)")]
        public string? PreferredCalendar { get; set; }

        /// <summary>A current IANA time zone id, <c>varchar(64)</c>; null for the tenant's time zone.</summary>
        [ValueSet(CoreValueSets.TimeZone)]
        [Column(TypeName = "varchar(64)")]
        public string? PreferredTimeZone { get; set; }

        /// <summary>
        ///     An upper-case ISO 3166-1 alpha-2 region, such as <c>AE</c>, <c>varchar(2)</c>, which picks
        ///     the user's formats; null for the tenant's region.
        /// </summary>
        [ValueSet(CoreValueSets.Region)]
        [Column(TypeName = "varchar(2)")]
        public string? PreferredRegion { get; set; }

        /// <summary>The digits the user reads numbers and dates in; null for the tenant's.</summary>
        public DigitStyle? PreferredDigits { get; set; }

        /// <summary>The grammatical gender the user is addressed in; null for neutral forms.</summary>
        public Gender? Gender { get; set; }

        /// <summary>The address the platform writes to, <c>nvarchar(255)</c>; null for <see cref="Email" />.</summary>
        [MaxLength(255)]
        public string? ContactEmail { get; set; }

        /// <summary>A mobile number in E.164 form, <c>varchar(32)</c>.</summary>
        [Column(TypeName = "varchar(32)")]
        public string? ContactMobile { get; set; }

        /// <summary>
        ///     Whether the user may use the tenant; server-owned. Every user is created active, and the
        ///     activate and deactivate actions alone change it.
        /// </summary>
        [ServerOwned]
        public bool IsActive { get; set; } = true;

        /// <summary>
        ///     The user's role memberships, saved with the user: null leaves them untouched, an empty list
        ///     deletes them all, and a list replaces them.
        /// </summary>
        [NotMapped]
        public IReadOnlyList<RoleMembership>? RoleMemberships { get; set; }
    }
}
