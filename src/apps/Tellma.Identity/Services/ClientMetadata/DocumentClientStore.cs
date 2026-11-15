// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using System.Globalization;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>The queries over document-client rows that the application manager does not offer: the count the cap is checked against, and the prune.</summary>
    public interface IDocumentClientStore
    {
        /// <summary>Counts the document-client rows.</summary>
        /// <param name="cancellationToken">Aborts the query.</param>
        /// <returns>How many exist.</returns>
        Task<int> CountAsync(CancellationToken cancellationToken);

        /// <summary>
        ///     Deletes the document-client rows that hold no authorization and no unexpired token
        ///     and were created before <paramref name="createdBefore" />, together with the expired
        ///     tokens such a row still holds.
        /// </summary>
        /// <param name="createdBefore">The creation cutoff.</param>
        /// <param name="now">The time a token's expiry is judged against.</param>
        /// <param name="cancellationToken">Aborts the prune.</param>
        /// <returns>How many rows were deleted.</returns>
        Task<int> PruneAsync(DateTimeOffset createdBefore, DateTimeOffset now, CancellationToken cancellationToken);
    }

    /// <summary>The engine's <see cref="IDocumentClientStore" /> over OpenIddict's applications table.</summary>
    /// <param name="context">The identity store.</param>
    public sealed class DocumentClientStore(TellmaIdentityDbContext context) : IDocumentClientStore
    {
        /// <summary>How many rows one delete statement names.</summary>
        public const int PruneBatchSize = 500;

        /// <summary>
        ///     The marker as it appears in a row's serialized properties. Only the document-client
        ///     factory writes this key, and only on a row whose client id is an <c>https</c> URL,
        ///     which a seeded or provisioned client never is; the query requires both.
        /// </summary>
        private const string MarkerFragment = "\"" + TellmaClientProperties.DocumentClient + "\"";

        /// <summary>The applications table.</summary>
        private IQueryable<OpenIddictEntityFrameworkCoreApplication> DocumentClients => context
            .Set<OpenIddictEntityFrameworkCoreApplication>()
            .Where(static application => application.ClientId!.StartsWith("https://")
                && application.Properties!.Contains(MarkerFragment));

        /// <inheritdoc />
        public Task<int> CountAsync(CancellationToken cancellationToken)
        {
            return DocumentClients.CountAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(DateTimeOffset createdBefore, DateTimeOffset now, CancellationToken cancellationToken)
        {
            // A client that was only ever fetched can still hold tokens of its own: a pushed
            // authorization request is stored as one, with no authorization behind it. An expired
            // one is no use to anybody and goes with the row; an unexpired one, or one with no
            // expiry, belongs to a flow that may still finish, and keeps the row until it lapses.
            DateTime cutoff = now.UtcDateTime;

            // The creation time lives in the serialized properties, so the unused rows are read
            // first and their creation time judged here. The buffer is a list: the connection
            // allows one open reader, and the deletes follow on it.
            var unused = await DocumentClients
                .Where(application => !application.Authorizations.Any()
                    && !application.Tokens.Any(token => token.ExpirationDate == null || token.ExpirationDate >= cutoff))
                .Select(static application => new { application.Id, application.Properties })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            List<string> expired = [.. unused
                .Where(row => CreatedUtc(row.Properties) is { } created && created < createdBefore)
                .Select(static row => row.Id!)];

            int deleted = 0;
            foreach (string[] batch in expired.Chunk(PruneBatchSize))
            {
                // The expired tokens first: the token table references the row. Each statement
                // re-checks that the row still holds no authorization, so a grant made since the
                // read keeps the row and every token it holds.
                await context.Set<OpenIddictEntityFrameworkCoreToken>()
                    .Where(token => batch.Contains(token.Application!.Id!)
                        && token.ExpirationDate < cutoff
                        && !token.Application!.Authorizations.Any())
                    .ExecuteDeleteAsync(cancellationToken);

                deleted += await context.Set<OpenIddictEntityFrameworkCoreApplication>()
                    .Where(application => batch.Contains(application.Id!)
                        && !application.Authorizations.Any()
                        && !application.Tokens.Any())
                    .ExecuteDeleteAsync(cancellationToken);
            }

            return deleted;
        }

        /// <summary>The creation time a row's serialized properties record, or null when they record none.</summary>
        /// <param name="properties">The serialized properties.</param>
        /// <returns>The creation time.</returns>
        internal static DateTimeOffset? CreatedUtc(string? properties)
        {
            if (string.IsNullOrEmpty(properties))
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(properties);
                return json.RootElement.ValueKind is JsonValueKind.Object
                    && json.RootElement.TryGetProperty(TellmaClientProperties.CreatedUtc, out JsonElement value)
                    && value.ValueKind is JsonValueKind.String
                    && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset created)
                        ? created
                        : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
