// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The in-proc identity engine's mail. The engine is a deployable without tenants: in a host of
    ///     its own it registers the never-sandboxed context, and its mail (sign-in codes, invitations,
    ///     password resets) addresses a platform user about their own account, never on a tenant's
    ///     behalf. In-proc it shares the distribution's container, whose sandbox flag answers the bound
    ///     tenant's category and refuses work bound to no tenant. This wiring gives the engine's mail
    ///     the answer of the engine's own host: never sandboxed, so it goes to the live channel
    ///     untouched, whatever its audience. Every other send outside a tenant still fails.
    /// </summary>
    /// <remarks>
    ///     No sandbox tenant's side effect reaches this path: a sandbox tenant's invitations ask the
    ///     identity server for an existing identity only, and the identity server sends nothing for
    ///     them.
    /// </remarks>
    internal static class InProcIdentityMail
    {
        /// <summary>The service key the engine resolves its sender by.</summary>
        public const string SenderKey = "Tellma.Identity";

        /// <summary>
        ///     Registers the engine's sender under its key, and wraps the composed sandbox flag so it
        ///     answers "never sandboxed" while that sender sends outside a tenant.
        /// </summary>
        /// <param name="services">The services, after the engine's registration.</param>
        public static void Register(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- The engine's sender: the composed pipeline, inside the engine's own sandbox answer ---
            // Added after the engine's default for the key, so it is the one the engine resolves.
            _ = services.AddScoped<IdentityMailScope>();
            _ = services.AddKeyedScoped<IEmailSender, IdentityMailSender>(SenderKey);

            // --- The sandbox flag: the composed one, except for the engine's mail outside a tenant ---
            ServiceDescriptor? composed = services.LastOrDefault(static descriptor => descriptor.ServiceType == typeof(ISandboxContext) && !descriptor.IsKeyedService);
            if (composed is null)
            {
                // Nothing to wrap: the deployment identity check reports the missing flag.
                return;
            }

            _ = services.RemoveAll<ISandboxContext>();
            _ = services.AddScoped<ISandboxContext>(provider => new IdentityMailSandboxContext(
                Create(provider, composed),
                provider.GetRequiredService<IRequestContextAccessor>(),
                provider.GetRequiredService<IdentityMailScope>()));
        }

        /// <summary>Creates the service a descriptor describes, whatever form it takes.</summary>
        /// <param name="provider">The scope's provider.</param>
        /// <param name="descriptor">The descriptor.</param>
        /// <returns>The service.</returns>
        private static ISandboxContext Create(IServiceProvider provider, ServiceDescriptor descriptor)
        {
            return (ISandboxContext)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
        }
    }

    /// <summary>Records, for one scope, that the engine's sender is sending.</summary>
    internal sealed class IdentityMailScope
    {
        /// <summary>How many of the engine's sends are under way in the scope.</summary>
        private int _depth;

        /// <summary>Whether one of the engine's sends is under way in the scope.</summary>
        public bool IsActive => Volatile.Read(ref _depth) > 0;

        /// <summary>Marks the start of one of the engine's sends.</summary>
        public void Enter()
        {
            _ = Interlocked.Increment(ref _depth);
        }

        /// <summary>Marks the end of one of the engine's sends.</summary>
        public void Exit()
        {
            _ = Interlocked.Decrement(ref _depth);
        }
    }

    /// <summary>
    ///     The sender the in-proc engine resolves: the scope's composed sender, the routing policy and
    ///     the transports included, with the scope marked as the engine's for the send's duration.
    /// </summary>
    /// <param name="services">The scope's provider.</param>
    /// <param name="scope">The scope's mark.</param>
    internal sealed class IdentityMailSender(IServiceProvider services, IdentityMailScope scope) : IEmailSender
    {
        /// <inheritdoc />
        public async Task<IReadOnlyList<EmailSendResult>> SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            // Resolved per send rather than injected, so the composed sender may be registered in any
            // order relative to the engine.
            IEmailSender pipeline = services.GetRequiredService<IEmailSender>();
            scope.Enter();
            try
            {
                return await pipeline.SendAsync(messages, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                scope.Exit();
            }
        }
    }

    /// <summary>
    ///     The composed sandbox flag, except while the engine's sender sends in a scope bound to no
    ///     tenant: that mail is the engine's, and the engine is never sandboxed.
    /// </summary>
    /// <param name="composed">The composed flag: the bound tenant's category.</param>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="scope">The scope's mark.</param>
    internal sealed class IdentityMailSandboxContext(ISandboxContext composed, IRequestContextAccessor accessor, IdentityMailScope scope) : ISandboxContext
    {
        /// <inheritdoc />
        public bool IsSandbox => (!scope.IsActive || accessor.Current.Tenant is not null) && composed.IsSandbox;
    }
}
