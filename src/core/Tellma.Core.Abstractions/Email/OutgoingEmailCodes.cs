// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     The <c>Email.*</c> codes of the outbox: the validation codes an enqueue or an administrator's
    ///     verb refuses with, and the error codes an outgoing email's row records. Each renders from
    ///     Core's resource its dots make (<c>Email_InvalidMessage</c>).
    /// </summary>
    public static class OutgoingEmailCodes
    {
        /// <summary>
        ///     An enqueued message is malformed: no primary recipient, a blank or malformed address, a
        ///     blank subject or text body, or a value wider than its column.
        /// </summary>
        public const string InvalidMessage = "Email.InvalidMessage";

        /// <summary>An enqueued message's To, Cc and Bcc hold more addresses than the outbox allows. Arguments: <c>count</c> and <c>max</c>.</summary>
        public const string TooManyRecipients = "Email.TooManyRecipients";

        /// <summary>An enqueued message's bodies and attachments take more bytes than the outbox allows. Arguments: <c>size</c> and <c>maxBytes</c>.</summary>
        public const string TooLarge = "Email.TooLarge";

        /// <summary>A row's error: the provider refused the message permanently.</summary>
        public const string Rejected = "Email.Rejected";

        /// <summary>A row's error: an attachment's file was missing from the store when the email was sent.</summary>
        public const string AttachmentMissing = "Email.AttachmentMissing";

        /// <summary>A row's error: the last send failed for a reason that may pass, and it is retried.</summary>
        public const string TransientFailure = "Email.TransientFailure";

        /// <summary>A row's error: every attempt failed for a reason that may pass.</summary>
        public const string AttemptsExhausted = "Email.AttemptsExhausted";

        /// <summary>A row's error: its send was interrupted and may have reached the recipients, so it was not repeated.</summary>
        public const string Interrupted = "Email.Interrupted";

        /// <summary>A retry targets an email that is neither failed with its job ended nor left without a live job.</summary>
        public const string NotRetryable = "Email.NotRetryable";

        /// <summary>A cancel targets an email that is no longer pending.</summary>
        public const string NotCancellable = "Email.NotCancellable";

        /// <summary>A delete targets an email that is still unsettled with a live job.</summary>
        public const string NotDeletable = "Email.NotDeletable";

        /// <summary>The argument naming how many addresses a message holds.</summary>
        public const string CountArgument = "count";

        /// <summary>The argument naming the most addresses a message may hold.</summary>
        public const string MaxArgument = "max";

        /// <summary>The argument naming how many bytes a message takes.</summary>
        public const string SizeArgument = "size";

        /// <summary>The argument naming the most bytes a message may take.</summary>
        public const string MaxBytesArgument = "maxBytes";
    }
}
