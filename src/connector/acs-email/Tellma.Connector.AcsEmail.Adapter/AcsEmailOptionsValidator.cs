// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.AcsEmail.Adapter
{
    /// <summary>Validates the ACS section when the transport is actually selected.</summary>
    /// <remarks>
    ///     Deliberately not registered with <c>ValidateOnStart</c>: only the active transport is
    ///     warmed, and a deployment still draining ACS delivery reports after migrating away has
    ///     webhook tokens and no send configuration, which is a legitimate state.
    /// </remarks>
    internal sealed class AcsEmailOptionsValidator : IValidateOptions<AcsEmailOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, AcsEmailOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            if (options.Endpoint is null)
            {
                failures.Add($"{AcsEmailOptions.SectionName}:{nameof(AcsEmailOptions.Endpoint)} is required.");
            }
            else if (!options.Endpoint.IsAbsoluteUri)
            {
                failures.Add($"{AcsEmailOptions.SectionName}:{nameof(AcsEmailOptions.Endpoint)} must be an absolute URI.");
            }

            if (string.IsNullOrWhiteSpace(options.From.Address))
            {
                failures.Add(
                    $"{AcsEmailOptions.SectionName}:{nameof(AcsEmailOptions.From)}:{nameof(EmailAddressOptions.Address)} is required.");
            }

            // ACS validates senderAddress against a configured MailFrom address and refuses anything
            // carrying a display name, which makes the sender's display name a property of the domain
            // resource rather than of a message. Rejecting the setting turns what would otherwise be
            // a silently ignored configuration into a startup failure that says where the name goes.
            if (!string.IsNullOrWhiteSpace(options.From.DisplayName))
            {
                failures.Add(
                    $"{AcsEmailOptions.SectionName}:{nameof(AcsEmailOptions.From)}:{nameof(EmailAddressOptions.DisplayName)} is not supported by this transport; set the display name on the sending domain's MailFrom address instead.");
            }

            if (options.MaxConcurrency < 1)
            {
                failures.Add($"{AcsEmailOptions.SectionName}:{nameof(AcsEmailOptions.MaxConcurrency)} must be at least 1.");
            }

            if (options.TimeoutSeconds <= 0)
            {
                failures.Add(
                    $"{AcsEmailOptions.SectionName}:{nameof(AcsEmailOptions.TimeoutSeconds)} must be greater than zero.");
            }

            return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
        }
    }
}
