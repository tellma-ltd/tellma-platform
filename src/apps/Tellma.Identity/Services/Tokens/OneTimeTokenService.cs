// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.Tokens
{
    /// <summary>The SQL-backed one-time token service.</summary>
    /// <param name="context">The identity store.</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class OneTimeTokenService(TellmaIdentityDbContext context, TimeProvider timeProvider) : IOneTimeTokenService
    {
        /// <inheritdoc />
        public async Task<string> IssueAsync(
            string userId,
            SingleUseCodePurpose purpose,
            TimeSpan lifetime,
            string? returnUrl,
            string? createdByClientId,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            DateTimeOffset now = timeProvider.GetUtcNow();

            // A fresh token supersedes any outstanding one of the same purpose.
            await context.Set<SingleUseCode>()
                .Where(code => code.UserId == userId && code.Purpose == purpose && code.ConsumedUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(static c => c.ConsumedUtc, now), cancellationToken);

            string id = Guid.NewGuid().ToString("N");
            string secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

            context.Set<SingleUseCode>().Add(new SingleUseCode
            {
                Id = id,
                UserId = userId,
                Purpose = purpose,
                SecretHash = Hash(secret),
                ReturnUrl = returnUrl,
                CreatedByClientId = createdByClientId,
                CreatedUtc = now,
                ExpiresUtc = now.Add(lifetime),
            });
            await context.SaveChangesAsync(cancellationToken);

            return id + "." + secret;
        }

        /// <inheritdoc />
        public async Task<OneTimeTokenContext?> RedeemAsync(
            string token, SingleUseCodePurpose purpose, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string[] parts = token.Split('.', 2);
            if (parts.Length != 2)
            {
                return null;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            SingleUseCode? stored = await context.Set<SingleUseCode>()
                .FirstOrDefaultAsync(
                    code => code.Id == parts[0] && code.Purpose == purpose && code.ConsumedUtc == null,
                    cancellationToken);

            if (stored is null || stored.ExpiresUtc <= now)
            {
                return null;
            }

            byte[] expected = Convert.FromBase64String(stored.SecretHash);
            byte[] actual = Convert.FromBase64String(Hash(parts[1]));
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                return null;
            }

            // Single-use under concurrency: only the conditional update's winner redeems it.
            int consumed = await context.Set<SingleUseCode>()
                .Where(code => code.Id == stored.Id && code.ConsumedUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(static c => c.ConsumedUtc, now), cancellationToken);

            return consumed == 1 ? new OneTimeTokenContext(stored.UserId, stored.ReturnUrl) : null;
        }

        /// <summary>Computes the stored hash of a token secret.</summary>
        internal static string Hash(string secret)
        {
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        }
    }
}
