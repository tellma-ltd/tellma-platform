// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Blobs
{
    // spec 0017 §4.2: one validator per stack whose aggregate carries a blob-reference column.
    /// <summary>
    ///     Refuses a save that attaches a blob it may not: every value the payload newly attaches, on
    ///     the root or on a child row at any depth, must name a staged, unexpired blob of the column's
    ///     kind that the saving user uploaded, and appear once in the whole payload. Reads the blob
    ///     rows on the validation round's batch; never touches storage. Deletes and actions validate
    ///     nothing: releasing a blob needs no check.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When the validator starts before the first round, the before images of the update rows
    ///         are not yet loaded, so every non-null value of an update row loads as a candidate and the
    ///         attach rule is applied once the round ran; an unchanged value is then left alone. When
    ///         the ids ride the first round the validator starts after it and the save has already
    ///         requested every blob row of the payload on that round, so the validator's exact request
    ///         is answered without a round of its own.
    ///     </para>
    ///     <para>
    ///         The load therefore costs no round trip whenever the save runs a first round: for update
    ///         rows, references, uniqueness, a tree or a cold id buffer. An insert-only save whose ids
    ///         come from a warm buffer and that otherwise runs no first round takes one validation round
    ///         for the blob rows it attaches.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TEntity">The stack's leaf.</typeparam>
    /// <param name="metadata">The entity metadata.</param>
    /// <param name="metrics">The attach instrument.</param>
    internal sealed class BlobReferenceValidator<TEntity>(IEntityMetadataProvider metadata, BlobAttachMetrics metrics) : IEntityValidator<TEntity>
        where TEntity : class
    {
        /// <inheritdoc />
        public async Task ValidateAsync(SaveContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            EntityMetadata root = metadata.Get(typeof(TEntity));
            if (BlobAttachments.Columns(root).Count == 0)
            {
                return;
            }

            // --- The candidates: what the payload attaches as far as the before images are known yet ---
            bool known = context.BeforeImages is not null;
            List<BlobAttach> candidates = Attached(context, root, known);
            if (candidates.Count == 0)
            {
                return;
            }

            // --- One load of every candidate's row, on the round's batch ---
            ContextRef<IReadOnlyDictionary<int, Blob>> blobs = context.Loader.ByIds<Blob, int>([.. candidates.Select(static attach => attach.Id).Distinct()], BlobAttachments.Select);
            await context.Loader.LoadAsync().ConfigureAwait(false);

            // --- The attach rule, once the before images are loaded ---
            IReadOnlyList<BlobAttach> attached = known ? candidates : Attached(context, root, known: true);
            Check(attached, blobs.Value, context);
        }

        /// <summary>
        ///     Refuses each newly attached value in payload order: a repeat of an id already attached
        ///     as a duplicate, and otherwise the first failing check of the blob's row.
        /// </summary>
        /// <param name="attached">The newly attached values, in payload order.</param>
        /// <param name="blobs">The loaded blob rows, by id; a missing id is absent.</param>
        /// <param name="context">The save's context.</param>
        private void Check(IReadOnlyList<BlobAttach> attached, IReadOnlyDictionary<int, Blob> blobs, SaveContext<TEntity> context)
        {
            HashSet<int> seen = [];
            foreach (BlobAttach attach in attached)
            {
                // --- One id attached twice anywhere in the payload: every occurrence after the first ---
                if (!seen.Add(attach.Id))
                {
                    context.Errors.Add(attach.Path, ValidationCodes.BlobDuplicateReference);
                    metrics.Rejected(attach.Reference.Kind, BlobTelemetryTagValues.AttachDuplicate);
                    continue;
                }

                if (Refusal(blobs.GetValueOrDefault(attach.Id), attach.Reference.Kind, context.Context.UserId, context.Context.Now) is string reason)
                {
                    context.Errors.Add(attach.Path, ValidationCodes.BlobNotAttachable, new Dictionary<string, object?>(StringComparer.Ordinal) { [BlobAttachments.ReasonArgument] = reason });
                    metrics.Rejected(attach.Reference.Kind, reason);
                }
            }
        }

        /// <summary>
        ///     Why a blob cannot be attached to a column, the first failing check in order: missing,
        ///     not staged, of another kind, staged by another user, or past its deadline.
        /// </summary>
        /// <param name="blob">The blob's row, or null when no row has the id.</param>
        /// <param name="kind">The column's kind.</param>
        /// <param name="userId">The saving user.</param>
        /// <param name="now">The instant the save started.</param>
        /// <returns>The reason, or null when the blob can be attached.</returns>
        internal static string? Refusal(Blob? blob, string kind, int? userId, DateTimeOffset now)
        {
            return blob switch
            {
                null => BlobTelemetryTagValues.AttachMissing,
                { State: not BlobState.Staged } => BlobTelemetryTagValues.AttachNotStaged,
                _ when !string.Equals(blob.Kind, kind, StringComparison.Ordinal) => BlobTelemetryTagValues.AttachKind,
                _ when blob.CreatedById != userId => BlobTelemetryTagValues.AttachUploader,
                { ExpiresAt: DateTimeOffset expires } when expires > now => null,
                _ => BlobTelemetryTagValues.AttachExpired,
            };
        }

        /// <summary>
        ///     The values the payload attaches: with the before images known, exactly what it newly
        ///     attaches; without them, every non-null value of an update row as well.
        /// </summary>
        /// <param name="context">The save's context.</param>
        /// <param name="root">The stack's root metadata.</param>
        /// <param name="known">Whether the before images are loaded.</param>
        /// <returns>The values, in payload order.</returns>
        private static List<BlobAttach> Attached(SaveContext<TEntity> context, EntityMetadata root, bool known)
        {
            List<BlobAttach> attached = [];
            for (int index = 0; index < context.Entities.Count; index++)
            {
                object? before = known && !context.IsNew(index) ? context.Before(index) : null;
                BlobAttachments.NewlyAttached(root, context.Entities[index], before, ValidationPath.Root.Index(index), attached);
            }

            return attached;
        }
    }
}
