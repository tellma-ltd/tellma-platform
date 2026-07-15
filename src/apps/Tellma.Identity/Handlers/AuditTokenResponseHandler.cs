// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Server;
using System.Text.Json;
using Tellma.Identity.Services.Audit;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Handlers
{
    /// <summary>
    ///     Audits every token-endpoint outcome — issuance and rejection alike — including
    ///     failure paths the pass-through controller never sees (bad client credentials, replayed
    ///     refresh tokens, permission rejections).
    /// </summary>
    /// <param name="auditLogger">Audit emission.</param>
    public sealed class AuditTokenResponseHandler(IAuditLogger auditLogger) : IOpenIddictServerHandler<ApplyTokenResponseContext>
    {
        /// <summary>The handler registration, late in the apply-response stage.</summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyTokenResponseContext>()
                .UseScopedHandler<AuditTokenResponseHandler>()
                .SetOrder(int.MaxValue - 100_000)
                .Build();

        /// <inheritdoc />
        public async ValueTask HandleAsync(ApplyTokenResponseContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            bool succeeded = string.IsNullOrEmpty(context.Response.Error);
            string details = JsonSerializer.Serialize(new
            {
                grantType = context.Request?.GrantType,
                error = context.Response.Error,
                errorDescription = context.Response.ErrorDescription,
            });

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = succeeded ? AuditActions.TokenIssued : AuditActions.TokenRequestRejected,
                ClientId = context.Request?.ClientId,
                Outcome = succeeded ? "success" : "failure",
                DetailsJson = details,
            });
        }
    }
}
