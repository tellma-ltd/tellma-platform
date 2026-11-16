// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Identity.Services.Audit;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     Writes the <see cref="AuditActions.ClientMetadataFetched" /> and
    ///     <see cref="AuditActions.ClientMetadataRejected" /> rows. Each write runs in a scope of
    ///     its own, because a fetch is shared by every request waiting on it and may outlive the
    ///     request that started it.
    /// </summary>
    /// <param name="scopeFactory">Creates the scope each row is written in.</param>
    public sealed class ClientMetadataAuditor(IServiceScopeFactory scopeFactory)
    {
        /// <summary>
        ///     The most characters of a client id the details record. The client id column holds
        ///     100; a longer client id is refused, and its refusal still shows what was presented.
        /// </summary>
        public const int MaxDetailsClientIdLength = 512;

        /// <summary>Records a fetch that passed validation.</summary>
        /// <param name="clientId">The client id.</param>
        /// <param name="host">The client id's host.</param>
        /// <param name="ipAddress">The address of the request that started the fetch.</param>
        /// <returns>A task that completes when the row is written (or its failure logged).</returns>
        public Task FetchedAsync(string clientId, string host, string? ipAddress)
        {
            return WriteAsync(AuditActions.ClientMetadataFetched, "success", clientId, ipAddress, new
            {
                host,
            });
        }

        /// <summary>Records a refusal.</summary>
        /// <param name="clientId">The client id as presented.</param>
        /// <param name="reason">The reason, one of <see cref="ClientMetadataRejectionReasons" />.</param>
        /// <param name="host">The client id's host, when it has one.</param>
        /// <param name="ipAddress">The address of the request refused.</param>
        /// <param name="servedCached">Whether a standing cached copy was served after the failure, so no request was refused.</param>
        /// <returns>A task that completes when the row is written (or its failure logged).</returns>
        public Task RejectedAsync(string clientId, string reason, string? host, string? ipAddress, bool servedCached = false)
        {
            ArgumentException.ThrowIfNullOrEmpty(reason);

            return WriteAsync(AuditActions.ClientMetadataRejected, "failure", clientId, ipAddress, new
            {
                reason,
                host,
                clientId = clientId.Length > MaxDetailsClientIdLength ? clientId[..MaxDetailsClientIdLength] : clientId,
                servedCached,
            });
        }

        /// <summary>Writes one row in a scope of its own.</summary>
        private async Task WriteAsync(string action, string outcome, string clientId, string? ipAddress, object details)
        {
            ArgumentNullException.ThrowIfNull(clientId);

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IAuditLogger auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

            // The column holds 100 characters; a longer client id is refused and lives in the details.
            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = action,
                ClientId = clientId.Length <= ClientIdUrl.MaxLength ? clientId : null,
                IpAddress = ipAddress,
                Outcome = outcome,
                DetailsJson = JsonSerializer.Serialize(details),
            }, CancellationToken.None);
        }
    }
}
