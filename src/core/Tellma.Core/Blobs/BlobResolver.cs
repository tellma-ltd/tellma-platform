// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Blobs
{
    // spec 0017 §5.1 and §5.2: resolve, authorise and combine, in one Read round trip.
    /// <summary>
    ///     Resolves the blob a download may serve: the caller's read of the owner, decided once per
    ///     composition, then one read batch carrying the owner row's existence under that read and
    ///     the blob row, combined in memory. "If you can read the owner you can read its blobs";
    ///     a staged blob is served to its uploader alone, owner or not. A blob served through a
    ///     sensitive owner read asks for the step-up a direct read of the owner asks for.
    /// </summary>
    /// <param name="kinds">The kind registry.</param>
    /// <param name="evaluator">Decides the caller's read of the owner.</param>
    /// <param name="securables">The securable registry the read is decided against.</param>
    /// <param name="runner">Runs the read behind the connect prologue.</param>
    /// <param name="context">The scope's request context, whose tenant settings shape the schema.</param>
    /// <param name="schemas">The query schemas the owner read is compiled against.</param>
    /// <param name="metadata">The entity metadata, which names the owner column's property.</param>
    /// <param name="stepUp">The assurance bar of the sensitive securables.</param>
    /// <param name="time">The clock a decision is made at.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class BlobResolver(
        IBlobKindRegistry kinds,
        AccessEvaluator evaluator,
        ISecurableRegistry securables,
        IGuardedBatchRunner runner,
        IRequestContextAccessor context,
        IQueryexSchemaProvider schemas,
        IEntityMetadataProvider metadata,
        StepUpBar stepUp,
        TimeProvider time,
        ILogger<BlobResolver> logger)
    {
        /// <summary>The declared parameter of the owner read: the blob's id.</summary>
        private static readonly QueryexParameterDeclaration IdParameter = new("id", QueryexType.QxNumeric, IsNotNull: true);

        /// <inheritdoc cref="IBlobService.ResolveAsync" />
        public async Task<BlobDownload?> ResolveAsync(string kind, int id, string? variant, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(kind);

            // --- 1. The kind, then the variant's form ---
            if (kinds.Find(kind) is not BlobKindDescriptor descriptor)
            {
                return null;
            }

            if (variant is not null && !BlobName.IsValidVariant(variant))
            {
                throw new BadRequestException("The variant is not a well-formed variant name.");
            }

            // --- 2. The caller's read of the owner, unless any member reads the kind ---
            // The decision is taken, recorded and counted here; a recomposition decides it again from
            // the fresh set, so a stale filter never reads the owner. A denial from a set older than
            // the fast-deny window is re-checked first, on a prologue-only round trip of its own.
            bool anyMember = descriptor.Policy.ReadAccess == BlobReadAccess.AnyMember;
            if (!anyMember)
            {
                _ = await DataAccessTranslation.RunAsync(() => evaluator.EvaluateAsync(descriptor.OwnerResource, AccessActions.Read, cancellationToken)).ConfigureAwait(false);
            }

            // --- 3. One read batch: the owner row under the read (skipped when denied), and the blob row.
            // Spec 0015 §14.2: a download runs outside the stack operations, so it translates its own
            // availability failures ---
            BatchResult<RowPage>? owner = null;
            BatchResult<ResolvedBlob?>? blob = null;
            AccessDecision? read = null;
            _ = await DataAccessTranslation.RunAsync(() => runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    batch.Limits = QueryexProfiles.PipelineLimits;
                    read = anyMember ? null : Decide(user, descriptor.OwnerResource);
                    owner = read is { Outcome: AccessOutcome.Denied } ? null : batch.Rows(OwnerRead(descriptor, read?.Filter), new QueryArguments().Add(IdParameter, (decimal)id), null);
                    DataBatch data = batch as DataBatch
                        ?? throw new InvalidOperationException("The guarded runner composed a batch that no tenant database created.");
                    blob = BlobStatements.Resolve(data, id, descriptor.Kind, user.UserId);
                    return ValueTask.CompletedTask;
                },
                static (_, _) => true,
                cancellationToken)).ConfigureAwait(false);

            // --- 4. The combination ---
            BlobDownload? download = Combine(descriptor, variant, blob!.Value, owner is { Value.Rows.RowCount: > 0 });

            // --- 5. The step-up a direct read of a sensitive owner asks for ---
            // Asked whenever the owner read was granted, found or not, so the answer tells nothing of
            // what the read found; only the uploader's own staged blob, served without the owner, is
            // outside it, as is the platform's own work.
            RequestContext current = context.Current;
            if (read is { Outcome: not AccessOutcome.Denied }
                && download is not { State: BlobState.Staged }
                && !current.IsSystem
                && securables.Find(descriptor.OwnerResource, AccessActions.Read) is { IsSensitive: true })
            {
                AssuranceCheck.Ensure(current, stepUp.Requirement, time.GetUtcNow());
            }

            if (download is null)
            {
                BlobServiceLog.DownloadNotFound(logger, descriptor.Kind, id);
            }

            return download;
        }

        // spec 0017 §5.1 step 4.
        /// <summary>
        ///     Combines the two reads: a committed blob is served when the caller reads an owner row
        ///     holding it, a staged one to its uploader alone (the read returned it only then), and a
        ///     variant only when the blob lists it and the kind declares its format.
        /// </summary>
        /// <param name="kind">The kind.</param>
        /// <param name="variant">The requested variant; null for the primary.</param>
        /// <param name="row">The blob row the read returned; null for none.</param>
        /// <param name="ownerVisible">Whether the owner read returned a row; false when it was skipped.</param>
        /// <returns>The download, or null when there is nothing to serve.</returns>
        internal static BlobDownload? Combine(BlobKindDescriptor kind, string? variant, ResolvedBlob? row, bool ownerVisible)
        {
            ArgumentNullException.ThrowIfNull(kind);

            // --- The row: committed under a readable owner, or staged (the read matched the uploader) ---
            bool servable = row switch
            {
                { State: BlobState.Committed } => ownerVisible,
                { State: BlobState.Staged } => true,
                _ => false,
            };
            if (!servable || row is null)
            {
                return null;
            }

            // --- The primary ---
            string etag = string.Create(CultureInfo.InvariantCulture, $"\"{row.Id}\"");
            if (variant is null)
            {
                return new BlobDownload(row.Id, row.Kind, row.State, row.StorageKey, row.ContentType, row.Size, row.FileName, IsImage(row.ContentType), etag);
            }

            // --- A variant the row lists, in the format the kind declares for it ---
            bool listed = row.Variants.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(variant, StringComparer.Ordinal);
            if (!listed || variant != ThumbnailPolicy.VariantName || kind.Policy.Thumbnail is not ThumbnailPolicy thumbnail)
            {
                return null;
            }

            string contentType = BlobContentTypes.ForImageFormat(thumbnail.Format);
            return new BlobDownload(
                row.Id,
                row.Kind,
                row.State,
                BlobName.Variant(row.StorageKey, variant),
                contentType,
                row.Size,
                row.FileName,
                IsImage(contentType),
                string.Create(CultureInfo.InvariantCulture, $"\"{row.Id}-{variant}\""));
        }

        /// <summary>Whether a media type is an image type.</summary>
        /// <param name="contentType">The media type.</param>
        /// <returns>True when it starts with <c>image/</c>.</returns>
        private static bool IsImage(string contentType)
        {
            return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The connected user's read of the owner resource, from the set the batch is composed for.</summary>
        /// <param name="user">The connected user.</param>
        /// <param name="resource">The owner resource.</param>
        /// <returns>The decision; a denial when the securable is not registered.</returns>
        private AccessDecision Decide(ConnectedUser user, string resource)
        {
            return securables.Find(resource, AccessActions.Read) is SecurableDescriptor securable
                ? evaluator.DecideFrom(user.Access, securable, time.GetUtcNow())
                : evaluator.Unknown(resource, AccessActions.Read);
        }

        // spec 0017 §5.1 step 3, statement 1: the owner entity's rows holding the id, under the read
        // filter seen from the owner through its path of parent navigations.
        /// <summary>The owner read: one owner row whose column holds the blob, under the caller's read filter.</summary>
        /// <param name="kind">The kind, with its owner entity, column and path.</param>
        /// <param name="filter">The read decision's filter; null when the read is unrestricted or any member reads the kind.</param>
        /// <returns>The query.</returns>
        private QuerySpec OwnerRead(BlobKindDescriptor kind, FilterTree? filter)
        {
            MultilingualShape shape = context.Current.TenantSettings?.Shape
                ?? throw new InvalidOperationException("The tenant's settings are not loaded in this scope yet, so the owner of a blob has no schema to be read under.");
            EntityDescriptor root = schemas.GetSchema(shape).FindEntity(kind.OwnerEntity)
                ?? throw new InvalidOperationException($"The owner entity {kind.OwnerEntity} of the blob kind '{kind.Kind}' is not in the query schema.");

            // The leaf names the column's property, which the query language reads.
            string property = metadata.All
                .First(entity => entity.Name == kind.OwnerEntity)
                .Properties.First(candidate => string.Equals(candidate.Column, kind.OwnerColumn, StringComparison.Ordinal))
                .Name;
            var column = FilterTree.Leaf($"{property} = @{IdParameter.Name}");
            FilterTree? access = filter is null ? null : kind.OwnerPath.Length > 0 ? FilterTree.Via(kind.OwnerPath, filter) : filter;

            // A page of one row must say its order; the key orders it.
            return new QuerySpec
            {
                Root = root,
                Select = "Id",
                OrderBy = "Id",
                Take = 1,
                Filter = access is null ? column : FilterTree.And([column, access]),
            };
        }
    }
}
