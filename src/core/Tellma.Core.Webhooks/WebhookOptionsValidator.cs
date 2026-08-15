// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;

namespace Tellma.Core.Webhooks
{
    /// <summary>Validates the webhook fronting's own configuration at startup.</summary>
    /// <remarks>
    ///     A cap of zero or a negative cap would answer every inbound webhook with a 413 — silently,
    ///     since the provider sees only a status code and the deployment sees only a metric that has
    ///     to be looked for. This fails the host instead.
    /// </remarks>
    internal sealed class WebhookOptionsValidator : IValidateOptions<WebhookOptions>
    {
        /// <summary>The smallest cap that can still accept a realistic provider payload.</summary>
        internal const long MinimumRequestBodyBytes = 1024;

        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, WebhookOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            return options.MaxRequestBodyBytes < MinimumRequestBodyBytes
                ? ValidateOptionsResult.Fail(
                    $"{WebhookOptions.SectionName}:{nameof(WebhookOptions.MaxRequestBodyBytes)} must be at least {MinimumRequestBodyBytes} bytes.")
                : ValidateOptionsResult.Success;
        }
    }
}
