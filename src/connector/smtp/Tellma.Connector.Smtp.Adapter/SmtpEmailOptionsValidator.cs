// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using MimeKit;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>
    ///     Validates the SMTP section and resolves both channels' settings once, so the send path
    ///     never re-derives a fallback.
    /// </summary>
    /// <remarks>
    ///     Deliberately not registered with <c>ValidateOnStart</c>. The pipeline warms only the
    ///     transport a deployment actually selected, which is what lets a distribution compile in
    ///     every adapter and configure one; eager validation here would make an unconfigured,
    ///     unselected adapter fail startup.
    /// </remarks>
    internal sealed class SmtpEmailOptionsValidator : IValidateOptions<SmtpEmailOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, SmtpEmailOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            if (string.IsNullOrWhiteSpace(options.Host))
            {
                failures.Add($"{SmtpEmailOptions.SectionName}:{nameof(SmtpEmailOptions.Host)} is required.");
            }

            ValidatePort(options.Port, $"{SmtpEmailOptions.SectionName}:{nameof(SmtpEmailOptions.Port)}", failures);
            ValidateTimeout(
                options.TimeoutSeconds,
                $"{SmtpEmailOptions.SectionName}:{nameof(SmtpEmailOptions.TimeoutSeconds)}",
                failures);
            ValidateFrom(options.From, $"{SmtpEmailOptions.SectionName}:{nameof(SmtpEmailOptions.From)}", failures);

            if (options.Sandbox is SmtpChannelOptions sandbox)
            {
                string prefix = $"{SmtpEmailOptions.SectionName}:{nameof(SmtpEmailOptions.Sandbox)}";
                if (string.IsNullOrWhiteSpace(sandbox.Host))
                {
                    failures.Add($"{prefix}:{nameof(SmtpChannelOptions.Host)} is required when the sandbox section is present.");
                }

                ValidatePort(sandbox.Port, $"{prefix}:{nameof(SmtpChannelOptions.Port)}", failures);
                ValidateTimeout(sandbox.TimeoutSeconds, $"{prefix}:{nameof(SmtpChannelOptions.TimeoutSeconds)}", failures);

                // Absent is legal: the sandbox channel inherits the live sender.
                if (sandbox.From is not null)
                {
                    ValidateFrom(sandbox.From, $"{prefix}:{nameof(SmtpChannelOptions.From)}", failures);
                }
            }

            return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
        }

        /// <summary>Flattens one channel's configuration into the settings the sender uses.</summary>
        /// <param name="options">The validated options.</param>
        /// <param name="channel">Which channel to resolve.</param>
        /// <returns>The resolved settings.</returns>
        /// <exception cref="InvalidOperationException">The sandbox channel was requested but is not
        ///     configured; the transport declares no sandbox factory in that case, so reaching this
        ///     would be a bug in the registration.</exception>
        internal static SmtpChannelSettings Resolve(SmtpEmailOptions options, SmtpChannel channel)
        {
            var liveFrom = options.From.ToEmailAddress();

            if (channel == SmtpChannel.Live)
            {
                return new SmtpChannelSettings(
                    options.Host!, options.Port, options.SecureSocket, options.Username, options.Password,
                    liveFrom, options.TimeoutSeconds);
            }

            SmtpChannelOptions sandbox = options.Sandbox
                ?? throw new InvalidOperationException(
                    $"{SmtpEmailOptions.SectionName}:{nameof(SmtpEmailOptions.Sandbox)} is not configured, so the SMTP transport has no sandbox channel.");

            return new SmtpChannelSettings(
                sandbox.Host!, sandbox.Port, sandbox.SecureSocket, sandbox.Username, sandbox.Password,
                sandbox.From?.ToEmailAddress() ?? liveFrom, sandbox.TimeoutSeconds);
        }

        private static void ValidatePort(int port, string key, List<string> failures)
        {
            if (port is < 1 or > 65535)
            {
                failures.Add($"{key} must be between 1 and 65535.");
            }
        }

        private static void ValidateTimeout(int timeoutSeconds, string key, List<string> failures)
        {
            if (timeoutSeconds <= 0)
            {
                failures.Add($"{key} must be greater than zero.");
            }
        }

        private static void ValidateFrom(EmailAddressOptions from, string key, List<string> failures)
        {
            if (string.IsNullOrWhiteSpace(from.Address))
            {
                failures.Add($"{key}:{nameof(EmailAddressOptions.Address)} is required.");
            }
            else if (!MailboxAddress.TryParse(from.Address, out _))
            {
                failures.Add($"{key}:{nameof(EmailAddressOptions.Address)} is not a valid mailbox address.");
            }
        }
    }
}
