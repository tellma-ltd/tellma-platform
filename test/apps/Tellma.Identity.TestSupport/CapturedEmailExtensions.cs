// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;

namespace Tellma.Identity.TestSupport
{
    /// <summary>
    ///     Reads identity's mail back out of the platform's capturing sender.
    ///     <para>
    ///         The shared sender deliberately collects and nothing more — what counts as "the code"
    ///         or "the link" is the sending product's business, not the test double's. These are
    ///         that knowledge for the identity server, in one place, because its two suites both
    ///         need it and each used to carry its own copy of a whole sender to get it.
    ///     </para>
    /// </summary>
    public static partial class CapturedEmailExtensions
    {
        /// <summary>How long a lookup waits for mail that is dispatched on a background worker.</summary>
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        /// <summary>The most recent message addressed to a recipient, or null when there is none.</summary>
        /// <param name="sender">The capturing sender.</param>
        /// <param name="email">The recipient's address.</param>
        /// <returns>The message, or null.</returns>
        public static EmailMessage? LatestFor(this CapturingEmailSender sender, string email)
        {
            ArgumentNullException.ThrowIfNull(sender);

            return sender.Captured
                .Where(captured => IsAddressedTo(captured, email))
                .Select(static captured => captured.Message)
                .LastOrDefault();
        }

        /// <summary>The sign-in code in the latest message to a recipient, or null.</summary>
        /// <param name="sender">The capturing sender.</param>
        /// <param name="email">The recipient's address.</param>
        /// <returns>The code, or null when no message or no code.</returns>
        public static string? LatestCodeFor(this CapturingEmailSender sender, string email)
        {
            return Extract(sender.LatestFor(email)?.TextBody, CodePattern());
        }

        /// <summary>The first link in the latest message to a recipient, or null.</summary>
        /// <param name="sender">The capturing sender.</param>
        /// <param name="email">The recipient's address.</param>
        /// <returns>The link, or null when no message or no link.</returns>
        public static string? LatestLinkFor(this CapturingEmailSender sender, string email)
        {
            return Extract(sender.LatestFor(email)?.TextBody, LinkPattern());
        }

        /// <summary>
        ///     Waits for mail to a recipient to arrive, then returns its body. Delivery is queued on
        ///     a background worker, so the request that triggered it returns first — this is the
        ///     event-driven wait for that, in place of a sleep loop that has to guess an interval.
        ///     <para>
        ///         Waits for <em>any</em> mail to the address, so it returns at once if this
        ///         recipient already has some. A test that sends twice to one address and wants the
        ///         second message must not use this — it would be handed the first one, and a code
        ///         or link already spent reads downstream as expired rather than as a stale read.
        ///         Take a baseline with <see cref="CountFor" /> and wait with
        ///         <see cref="WaitForCodeAfterAsync" />, or <see cref="CapturingEmailSender.Clear" />
        ///         in between where the sender belongs to one test.
        ///     </para>
        /// </summary>
        /// <param name="sender">The capturing sender.</param>
        /// <param name="email">The recipient's address.</param>
        /// <param name="timeout">How long to wait; the default suits a local worker.</param>
        /// <returns>The message body.</returns>
        public static async Task<string> WaitForBodyAsync(
            this CapturingEmailSender sender, string email, TimeSpan? timeout = null)
        {
            ArgumentNullException.ThrowIfNull(sender);

            await sender.WaitForAsync(
                captured => captured.Any(one => IsAddressedTo(one, email)),
                timeout ?? DefaultTimeout);

            return sender.LatestFor(email)!.TextBody;
        }

        /// <summary>Waits for mail to a recipient and returns the sign-in code in it.</summary>
        /// <param name="sender">The capturing sender.</param>
        /// <param name="email">The recipient's address.</param>
        /// <param name="timeout">How long to wait; the default suits a local worker.</param>
        /// <returns>The code.</returns>
        public static async Task<string> WaitForCodeAsync(
            this CapturingEmailSender sender, string email, TimeSpan? timeout = null)
        {
            string body = await sender.WaitForBodyAsync(email, timeout);
            return Extract(body, CodePattern())
                ?? throw new InvalidOperationException($"The message to {email} carries no sign-in code: {body}");
        }

        /// <summary>
        ///     How many messages have reached a recipient so far. The baseline a test takes before
        ///     provoking another one, so the wait that follows can tell the new message from the one
        ///     already sitting there.
        /// </summary>
        /// <param name="sender">The capturing sender.</param>
        /// <param name="email">The recipient's address.</param>
        /// <returns>The count; zero when nothing has been sent to that address.</returns>
        public static int CountFor(this CapturingEmailSender sender, string email)
        {
            ArgumentNullException.ThrowIfNull(sender);

            return sender.Captured.Count(captured => IsAddressedTo(captured, email));
        }

        /// <summary>
        ///     Waits for a message beyond a baseline and returns the sign-in code in it — the form
        ///     to use when the recipient already has mail, since the plain wait would return at once
        ///     with the older one.
        /// </summary>
        /// <param name="sender">The capturing sender.</param>
        /// <param name="email">The recipient's address.</param>
        /// <param name="since">The count from <see cref="CountFor" />, taken before provoking the message.</param>
        /// <param name="timeout">How long to wait; the default suits a local worker.</param>
        /// <returns>The code from the first message to arrive past the baseline.</returns>
        public static async Task<string> WaitForCodeAfterAsync(
            this CapturingEmailSender sender, string email, int since, TimeSpan? timeout = null)
        {
            ArgumentNullException.ThrowIfNull(sender);

            await sender.WaitForAsync(
                captured => captured.Count(one => IsAddressedTo(one, email)) > since,
                timeout ?? DefaultTimeout);

            string body = sender.LatestFor(email)!.TextBody;
            return Extract(body, CodePattern())
                ?? throw new InvalidOperationException($"The message to {email} carries no sign-in code: {body}");
        }

        /// <summary>Waits for mail to a recipient and returns the first link in it.</summary>
        /// <param name="sender">The capturing sender.</param>
        /// <param name="email">The recipient's address.</param>
        /// <param name="timeout">How long to wait; the default suits a local worker.</param>
        /// <returns>The link.</returns>
        public static async Task<string> WaitForLinkAsync(
            this CapturingEmailSender sender, string email, TimeSpan? timeout = null)
        {
            string body = await sender.WaitForBodyAsync(email, timeout);
            return Extract(body, LinkPattern())
                ?? throw new InvalidOperationException($"The message to {email} carries no link: {body}");
        }

        /// <summary>
        ///     Whether a captured message went to this address. Matches on the address alone: the
        ///     recipient carries a display name too, and the To list is a list even when identity
        ///     only ever puts one entry in it.
        /// </summary>
        private static bool IsAddressedTo(CapturedEmail captured, string email)
        {
            return captured.Message.To.Any(
                address => string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Applies a pattern to a body, tolerating a body that is not there.</summary>
        private static string? Extract(string? body, Regex pattern)
        {
            if (body is null)
            {
                return null;
            }

            Match match = pattern.Match(body);
            return match.Success ? match.Value : null;
        }

        /// <summary>The engine's sign-in codes are eight digits.</summary>
        [GeneratedRegex(@"\b\d{8}\b")]
        private static partial Regex CodePattern();

        /// <summary>Invitation, recovery and reset links; the templates put nothing after them.</summary>
        [GeneratedRegex(@"https?://\S+")]
        private static partial Regex LinkPattern();
    }
}
