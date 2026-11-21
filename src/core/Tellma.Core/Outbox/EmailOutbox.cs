// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using System.Text;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Blobs;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §3.2: resolve every blob attachment, validate every request, stage before the frame
    // persists, then enlist one Pending row per request. The frame's validation round checks the
    // staged blobs against the attach rule, its persist confirms them, and the stack's persist hook
    // enqueues each row's sending job, so the rows, the blobs' confirmation and the jobs commit with
    // the frame's transaction. Spec 0015 §13.3 admits the outbox in every frame.
    /// <summary>
    ///     The scoped outbox: validates the requests, stages their attachments as blobs of the
    ///     <c>email-attachment</c> kind, and enlists one <see cref="OutgoingEmail" /> row per request
    ///     with the caller's frame.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every refusal of a request is collected before anything is staged and thrown as one
    ///         <see cref="ValidationException" />, its errors at the request's index (<c>[i]</c>) and,
    ///         for a blob attachment, at <c>[i].BlobAttachments[j]</c>.
    ///     </para>
    ///     <para>
    ///         The blob service and store are resolved only for a call with attachments, so a scope
    ///         without a blob store, such as the startup gate's, still creates the outbox and every
    ///         channel that sends through it.
    ///     </para>
    /// </remarks>
    /// <param name="services">The scope's services, which the blob service and store are resolved from when a call has attachments.</param>
    /// <param name="accessor">The scope's request context, which names the tenant whose store a copied blob is read from.</param>
    /// <param name="options">The outbox's limits.</param>
    /// <param name="metrics">The outbox's instruments, which note each row's source for its count once its frame commits.</param>
    internal sealed class EmailOutbox(IServiceProvider services, IRequestContextAccessor accessor, IOptions<OutboxOptions> options, OutboxMetrics metrics)
        : IEmailOutbox, IEnlists<OutgoingEmail>
    {
        /// <summary>The arguments of a code that has none.</summary>
        private static readonly IReadOnlyDictionary<string, object?> NoArguments = FrozenDictionary<string, object?>.Empty;

        /// <inheritdoc />
        public async Task<EnlistedSave<OutgoingEmail>> EnqueueAsync(IWriteHost host, IReadOnlyList<EmailEnqueueRequest> requests, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(requests);
            if (requests.Count == 0)
            {
                throw new ArgumentException("Enqueue at least one email.", nameof(requests));
            }

            for (int index = 0; index < requests.Count; index++)
            {
                if (requests[index]?.Message is null)
                {
                    throw new ArgumentException($"The request at {index} is null or carries no message.", nameof(requests));
                }

                // The source tags the enqueue counter, whose values are a closed set.
                if (requests[index].Source is not (EmailEnqueueRequest.ApiSource or EmailEnqueueRequest.NotificationSource))
                {
                    throw new ArgumentException(
                        $"The request at {index} names the source '{requests[index].Source}'; a request's source is '{EmailEnqueueRequest.ApiSource}' or '{EmailEnqueueRequest.NotificationSource}'.",
                        nameof(requests));
                }
            }

            OutboxOptions limits = options.Value;
            IBlobService? blobs = requests.Any(static request => request.Message.Attachments is { Count: > 0 } || request.BlobAttachments is { Count: > 0 })
                ? services.GetRequiredService<IBlobService>()
                : null;

            // --- Step 1: resolve every blob attachment, then validate every request ---
            List<ValidationError> errors = [];
            var resolved = new BlobDownload?[requests.Count][];
            for (int index = 0; index < requests.Count; index++)
            {
                resolved[index] = await ResolveAsync(blobs, requests[index], index, errors, cancellationToken).ConfigureAwait(false);
            }

            for (int index = 0; index < requests.Count; index++)
            {
                Validate(requests[index], resolved[index], ValidationPath.Root.Index(index), limits, errors);
            }

            if (errors.Count > 0)
            {
                throw new ValidationException(errors);
            }

            // --- Step 2: stage the attachments, before the frame persists ---
            // Step 3: one Pending row per request, its addresses formatted as RFC 5322 lists.
            var rows = new OutgoingEmail[requests.Count];
            for (int index = 0; index < requests.Count; index++)
            {
                IReadOnlyList<OutgoingEmailAttachment>? attachments = blobs is null
                    ? null
                    : await StageAsync(blobs, requests[index], resolved[index], ValidationPath.Root.Index(index), cancellationToken).ConfigureAwait(false);
                rows[index] = Row(requests[index], attachments);
            }

            // --- Enlist with the caller's frame: the rows, their blobs and their jobs commit with it ---
            // Spec 0022 §9.2 counts one per row, by its request's source, once the frame committed
            // them: the rows' sources are noted here and counted by the stack's commit effect.
            metrics.Took(rows, requests);
            return await this.EnlistSaveAsync(host, rows).ConfigureAwait(false);
        }

        /// <summary>
        ///     Whether a message can be stored and sent: a primary recipient, every address well formed,
        ///     a single-line subject that fits its column, a text body, named and typed attachments, at
        ///     most <see cref="OutgoingEmail.MaxAttachments" /> attachments in all, and a regarding
        ///     resource that fits its column.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>True when the request is well formed.</returns>
        internal static bool IsWellFormed(EmailEnqueueRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            EmailMessage message = request.Message;

            // --- The transport's structural rules: a recipient, no blank address, named and typed attachments ---
            if (!EmailMessageValidation.TryValidate(message, out _))
            {
                return false;
            }

            // --- Every address survives the stored list and can never break a header ---
            if (!message.To.All(EmailAddressList.IsWellFormed)
                || !message.Cc.All(EmailAddressList.IsWellFormed)
                || !message.Bcc.All(EmailAddressList.IsWellFormed)
                || !IsSingleAddress(message.From)
                || !IsSingleAddress(message.ReplyTo))
            {
                return false;
            }

            // --- A subject on one line that fits its column, and a text body ---
            if (string.IsNullOrWhiteSpace(message.Subject)
                || message.Subject.Length > OutgoingEmail.SubjectLength
                || message.Subject.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(message.TextBody))
            {
                return false;
            }

            // --- The attachments: at most the row's children, each content id fitting its column ---
            IReadOnlyList<EmailBlobAttachment> references = request.BlobAttachments ?? [];
            if (message.Attachments.Count + references.Count > OutgoingEmail.MaxAttachments
                || !message.Attachments.All(static attachment => IsContentId(attachment.ContentId))
                || !references.All(static reference => reference is not null && !string.IsNullOrWhiteSpace(reference.Kind) && IsContentId(reference.ContentId)))
            {
                return false;
            }

            // --- The regarding record: a resource name that fits its non-Unicode column ---
            return request.RegardingEntity is not string regarding
                || (regarding.Length is > 0 and <= OutgoingEmail.RegardingEntityLength && regarding.All(static c => char.IsAsciiLetterOrDigit(c) || c == '.'));
        }

        /// <summary>The bytes a message's bodies take, UTF-8 encoded, as a transport sends them.</summary>
        /// <param name="message">The message.</param>
        /// <returns>The bytes.</returns>
        internal static long BodyBytes(EmailMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            return Encoding.UTF8.GetByteCount(message.TextBody) + (message.HtmlBody is string html ? Encoding.UTF8.GetByteCount(html) : 0);
        }

        /// <summary>
        ///     Validates one request: <see cref="OutgoingEmailCodes.InvalidMessage" /> for a malformed
        ///     one, else <see cref="OutgoingEmailCodes.TooManyRecipients" /> and
        ///     <see cref="OutgoingEmailCodes.TooLarge" /> as they apply.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="resolved">Its blob attachments as resolved, in order; null where one did not resolve.</param>
        /// <param name="at">The request's path.</param>
        /// <param name="limits">The outbox's limits.</param>
        /// <param name="errors">Receives the errors.</param>
        internal static void Validate(EmailEnqueueRequest request, IReadOnlyList<BlobDownload?> resolved, ValidationPath at, OutboxOptions limits, List<ValidationError> errors)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(resolved);
            ArgumentNullException.ThrowIfNull(at);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(errors);

            // --- A malformed message is judged no further: its counts mean nothing ---
            if (!IsWellFormed(request))
            {
                errors.Add(new ValidationError(at, OutgoingEmailCodes.InvalidMessage, NoArguments));
                return;
            }

            // --- The recipients, To, Cc and Bcc together ---
            EmailMessage message = request.Message;
            int count = message.To.Count + message.Cc.Count + message.Bcc.Count;
            if (count > limits.MaxRecipients)
            {
                errors.Add(new ValidationError(at, OutgoingEmailCodes.TooManyRecipients, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [OutgoingEmailCodes.CountArgument] = count,
                    [OutgoingEmailCodes.MaxArgument] = limits.MaxRecipients,
                }));
            }

            // --- The size: the bodies, every message attachment and every resolved blob's stored size ---
            long size = BodyBytes(message)
                + message.Attachments.Sum(static attachment => (long)attachment.Content.Length)
                + resolved.Sum(static download => download?.Size ?? 0);
            if (size > limits.MaxMessageBytes)
            {
                errors.Add(new ValidationError(at, OutgoingEmailCodes.TooLarge, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [OutgoingEmailCodes.SizeArgument] = size,
                    [OutgoingEmailCodes.MaxBytesArgument] = limits.MaxMessageBytes,
                }));
            }
        }

        /// <summary>The pending row of one request.</summary>
        /// <param name="request">The request.</param>
        /// <param name="attachments">The staged attachments; null for none.</param>
        /// <returns>The row.</returns>
        internal static OutgoingEmail Row(EmailEnqueueRequest request, IReadOnlyList<OutgoingEmailAttachment>? attachments)
        {
            ArgumentNullException.ThrowIfNull(request);
            EmailMessage message = request.Message;
            return new OutgoingEmail
            {
                State = OutgoingEmailState.Pending,
                Audience = message.Audience,
                From = message.From is EmailAddress from ? EmailAddressList.Format(from) : null,
                ReplyTo = message.ReplyTo is EmailAddress replyTo ? EmailAddressList.Format(replyTo) : null,
                To = EmailAddressList.Format(message.To)!,
                Cc = EmailAddressList.Format(message.Cc),
                Bcc = EmailAddressList.Format(message.Bcc),
                Subject = message.Subject,
                TextBody = message.TextBody,
                HtmlBody = message.HtmlBody,
                RecipientUserId = request.RecipientUserId,
                RegardingEntity = request.RegardingEntity,
                RegardingId = request.RegardingId,
                SendAfter = request.SendAfter,
                OutgoingEmailAttachments = attachments is { Count: > 0 } ? attachments : null,
            };
        }

        /// <summary>
        ///     Resolves a request's blob attachments, each in one round trip, refusing one that does not
        ///     resolve (<c>missing</c>) and a staged one of another kind than an email attachment's
        ///     (<c>kind</c>) with <see cref="ValidationCodes.BlobNotAttachable" />.
        /// </summary>
        /// <param name="blobs">The blob service; null when no request attaches anything.</param>
        /// <param name="request">The request.</param>
        /// <param name="index">The request's index.</param>
        /// <param name="errors">Receives the refusals.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        /// <returns>The resolved blobs, in order; null where one was refused.</returns>
        private static async Task<BlobDownload?[]> ResolveAsync(IBlobService? blobs, EmailEnqueueRequest request, int index, List<ValidationError> errors, CancellationToken cancellationToken)
        {
            if (blobs is null || request.BlobAttachments is not { Count: > 0 } references)
            {
                return [];
            }

            var resolved = new BlobDownload?[references.Count];
            for (int position = 0; position < references.Count; position++)
            {
                // A malformed reference is the request's InvalidMessage; it resolves nothing.
                EmailBlobAttachment? reference = references[position];
                if (reference is null || string.IsNullOrWhiteSpace(reference.Kind))
                {
                    continue;
                }

                BlobDownload? download = await blobs.ResolveAsync(reference.Kind, reference.BlobId, variant: null, cancellationToken).ConfigureAwait(false);
                string? refusal = download switch
                {
                    null => BlobTelemetryTagValues.AttachMissing,
                    { State: BlobState.Staged } when !string.Equals(download.Kind, OutgoingEmailAttachment.BlobKind, StringComparison.Ordinal) => BlobTelemetryTagValues.AttachKind,
                    _ => null,
                };

                if (refusal is null)
                {
                    resolved[position] = download;
                }
                else
                {
                    errors.Add(NotAttachable(ValidationPath.Root.Index(index), position, refusal));
                }
            }

            return resolved;
        }

        /// <summary>
        ///     Stages a request's attachments: each of the message's own files, then each blob
        ///     attachment, a staged email attachment as it is and a committed blob as a copy.
        /// </summary>
        /// <param name="blobs">The blob service.</param>
        /// <param name="request">The request.</param>
        /// <param name="resolved">Its blob attachments as resolved, in order.</param>
        /// <param name="at">The request's path.</param>
        /// <param name="cancellationToken">Cancels the staging.</param>
        /// <returns>The attachment rows, in order.</returns>
        /// <exception cref="ValidationException">A committed blob's object is missing from the store.</exception>
        private async Task<IReadOnlyList<OutgoingEmailAttachment>> StageAsync(
            IBlobService blobs, EmailEnqueueRequest request, BlobDownload?[] resolved, ValidationPath at, CancellationToken cancellationToken)
        {
            List<OutgoingEmailAttachment> attachments = [];

            // --- The message's own files: one round trip and one store write each ---
            foreach (EmailAttachment attachment in request.Message.Attachments)
            {
                using Stream content = Open(attachment.Content);
                BlobDescriptor staged = await blobs.StageAsync(
                    new BlobStageRequest(OutgoingEmailAttachment.BlobKind, content, attachment.Content.Length, attachment.ContentType, attachment.FileName),
                    cancellationToken).ConfigureAwait(false);
                attachments.Add(Attachment(staged.Id, staged.FileName, attachment.ContentId));
            }

            // --- The blob attachments: an uploaded email attachment as it is, any committed blob copied ---
            IReadOnlyList<EmailBlobAttachment> references = request.BlobAttachments ?? [];
            for (int position = 0; position < references.Count; position++)
            {
                EmailBlobAttachment reference = references[position];
                BlobDownload download = resolved[position] ?? throw new InvalidOperationException("A blob attachment that did not resolve was staged.");
                if (download.State == BlobState.Staged)
                {
                    attachments.Add(Attachment(download.Id, BlobFileNames.Sanitize(reference.FileName) ?? download.FileName, reference.ContentId));
                    continue;
                }

                BlobDescriptor copy = await CopyAsync(blobs, download, reference, at, position, cancellationToken).ConfigureAwait(false);
                attachments.Add(Attachment(copy.Id, copy.FileName, reference.ContentId));
            }

            return attachments;
        }

        /// <summary>Copies a committed blob into a new staged email attachment, through the store and the stager.</summary>
        /// <param name="blobs">The blob service.</param>
        /// <param name="download">The committed blob.</param>
        /// <param name="reference">The attachment's reference.</param>
        /// <param name="at">The request's path.</param>
        /// <param name="position">The attachment's position among the request's blob attachments.</param>
        /// <param name="cancellationToken">Cancels the copy.</param>
        /// <returns>The staged copy.</returns>
        /// <exception cref="ValidationException">The blob's object is missing from the store.</exception>
        private async Task<BlobDescriptor> CopyAsync(
            IBlobService blobs, BlobDownload download, EmailBlobAttachment reference, ValidationPath at, int position, CancellationToken cancellationToken)
        {
            IBlobStore store = services.GetRequiredService<IBlobStore>();
            int tenantId = accessor.Current.RequireTenant().Id;
            BlobContent content = await store.OpenReadAsync(tenantId, download.StorageName, cancellationToken).ConfigureAwait(false)
                ?? throw new ValidationException([NotAttachable(at, position, BlobTelemetryTagValues.AttachMissing)]);
            await using (content.ConfigureAwait(false))
            {
                return await blobs.StageAsync(
                    new BlobStageRequest(OutgoingEmailAttachment.BlobKind, content.Content, content.Length, download.ContentType, reference.FileName ?? download.FileName),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>An attachment row over a staged blob.</summary>
        /// <param name="blobId">The staged blob.</param>
        /// <param name="fileName">The name the recipient sees; null for the placeholder.</param>
        /// <param name="contentId">The inline content id; null for none.</param>
        /// <returns>The row.</returns>
        private static OutgoingEmailAttachment Attachment(int blobId, string? fileName, string? contentId)
        {
            return new OutgoingEmailAttachment
            {
                FileId = blobId,
                FileName = fileName ?? BlobFileNames.Placeholder,
                ContentId = string.IsNullOrWhiteSpace(contentId) ? null : contentId,
            };
        }

        /// <summary>A read-only stream over an attachment's bytes, without copying them when they sit in an array.</summary>
        /// <param name="content">The bytes.</param>
        /// <returns>The stream.</returns>
        private static MemoryStream Open(ReadOnlyMemory<byte> content)
        {
            return MemoryMarshal.TryGetArray(content, out ArraySegment<byte> segment) && segment.Array is byte[] array
                ? new MemoryStream(array, segment.Offset, segment.Count, writable: false)
                : new MemoryStream(content.ToArray(), writable: false);
        }

        /// <summary>The refusal of one blob attachment.</summary>
        /// <param name="at">The request's path.</param>
        /// <param name="position">The attachment's position among the request's blob attachments.</param>
        /// <param name="reason">Why it cannot be attached.</param>
        /// <returns>The error.</returns>
        private static ValidationError NotAttachable(ValidationPath at, int position, string reason)
        {
            return new ValidationError(
                at.Property(nameof(EmailEnqueueRequest.BlobAttachments)).Index(position),
                ValidationCodes.BlobNotAttachable,
                new Dictionary<string, object?>(StringComparer.Ordinal) { [BlobAttachments.ReasonArgument] = reason });
        }

        /// <summary>Whether an optional single address is absent, or well formed and fits its column once formatted.</summary>
        /// <param name="address">The address; null for none.</param>
        /// <returns>True when it can be stored.</returns>
        private static bool IsSingleAddress(EmailAddress? address)
        {
            return address is null || (EmailAddressList.IsWellFormed(address) && EmailAddressList.Format(address).Length <= OutgoingEmail.AddressLength);
        }

        /// <summary>Whether an optional content id is absent, or fits its column on one line.</summary>
        /// <param name="contentId">The content id; null for none.</param>
        /// <returns>True when it can be stored.</returns>
        private static bool IsContentId(string? contentId)
        {
            return contentId is null || (contentId.Length <= OutgoingEmailAttachment.NameLength && !contentId.Any(char.IsControl));
        }
    }
}
