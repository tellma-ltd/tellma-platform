// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     Declares one email transport for configuration-driven selection. Adapters register an
    ///     instance in DI; the platform email pipeline picks the one whose <see cref="Name" /> matches
    ///     the configured provider and exposes it to consumers as the <see cref="IEmailSender" />.
    /// </summary>
    /// <remarks>
    ///     The shape of <see cref="Name" /> and the uniqueness of names across the composition are
    ///     validated at startup rather than here, so a misconfigured host sees every problem in one
    ///     diagnostic instead of failing on whichever registration happened to run first.
    /// </remarks>
    /// <param name="Name">The transport's stable configuration name (lowercase kebab-case:
    ///     "sendgrid", "smtp", "log-sink"). Matched case-insensitively; unique across the
    ///     composition, validated at startup.</param>
    /// <param name="Live">Creates the live sender — real delivery with the transport's live
    ///     configuration.</param>
    /// <param name="Sandbox">Creates the sandbox-delivery sender — the transport's no-real-delivery
    ///     channel (a validation-only provider mode, a mail-trap host) — or null when the transport
    ///     has none, in which case the pipeline withholds external sandbox mail itself.</param>
    public sealed record EmailTransportRegistration(
        string Name,
        Func<IServiceProvider, IEmailSender> Live,
        Func<IServiceProvider, IEmailSender>? Sandbox = null);
}
