// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.SendGrid.Adapter
{
    /// <summary>Validates the SendGrid section when the transport is actually selected.</summary>
    /// <remarks>
    ///     Deliberately not registered with <c>ValidateOnStart</c>: the pipeline warms only the
    ///     active transport, so a referenced-but-unselected SendGrid adapter must not fail startup
    ///     for configuration nobody asked it to have. Note in particular that the event-webhook keys
    ///     are validated where they are read at composition, not here — a deployment that has
    ///     migrated away from SendGrid but is still draining its in-flight events has webhook
    ///     configuration and no API key, and that is a legitimate state.
    /// </remarks>
    internal sealed class SendGridEmailOptionsValidator : IValidateOptions<SendGridEmailOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, SendGridEmailOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                failures.Add($"{SendGridEmailOptions.SectionName}:{nameof(SendGridEmailOptions.ApiKey)} is required.");
            }

            if (string.IsNullOrWhiteSpace(options.From.Address))
            {
                failures.Add(
                    $"{SendGridEmailOptions.SectionName}:{nameof(SendGridEmailOptions.From)}:{nameof(EmailAddressOptions.Address)} is required.");
            }

            if (options.MaxConcurrency < 1)
            {
                failures.Add(
                    $"{SendGridEmailOptions.SectionName}:{nameof(SendGridEmailOptions.MaxConcurrency)} must be at least 1.");
            }

            if (options.TimeoutSeconds <= 0)
            {
                failures.Add(
                    $"{SendGridEmailOptions.SectionName}:{nameof(SendGridEmailOptions.TimeoutSeconds)} must be greater than zero.");
            }

            return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
        }
    }
}
