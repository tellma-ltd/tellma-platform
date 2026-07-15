// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.Services.Audit;

namespace Tellma.Identity.Services.Tap
{
    /// <summary>The SQL-backed Temporary Access Pass service.</summary>
    /// <param name="context">The identity store.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class TemporaryAccessPassService(
        TellmaIdentityDbContext context,
        UserManager<TellmaIdentityUser> userManager,
        IAuditLogger auditLogger,
        TimeProvider timeProvider) : ITemporaryAccessPassService
    {
        /// <summary>The pass lifetime (the maximum permitted).</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

        /// <inheritdoc />
        public async Task<IssuedTemporaryAccessPass?> IssueAsync(
            string userId, string? issuedByClientId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            TellmaIdentityUser? user = await userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return null;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();

            // Supersede any outstanding pass.
            await context.Set<TemporaryAccessPass>()
                .Where(pass => pass.UserId == userId && pass.ConsumedUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(static p => p.ConsumedUtc, now), cancellationToken);

            // A readable grouped pass (for example ABCD-EFGH-JK); ambiguous characters excluded.
            string clear = FormatPass(RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 10));

            context.Set<TemporaryAccessPass>().Add(new TemporaryAccessPass
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                SecretHash = Hash(userId, clear),
                IssuedByClientId = issuedByClientId,
                CreatedUtc = now,
                ExpiresUtc = now.Add(Lifetime),
            });
            await context.SaveChangesAsync(cancellationToken);

            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.TapIssued,
                    Subject = userId,
                    ClientId = issuedByClientId,
                    Outcome = "success",
                },
                cancellationToken);

            return new IssuedTemporaryAccessPass(clear, now.Add(Lifetime));
        }

        /// <inheritdoc />
        public async Task<string?> RedeemAsync(string email, string pass, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(email);
            ArgumentException.ThrowIfNullOrWhiteSpace(pass);

            TellmaIdentityUser? user = await userManager.FindByEmailAsync(email);
            if (user is null || user.LifecycleState != UserLifecycleState.Active)
            {
                return null;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            TemporaryAccessPass? stored = await context.Set<TemporaryAccessPass>()
                .Where(p => p.UserId == user.Id && p.ConsumedUtc == null)
                .OrderByDescending(static p => p.CreatedUtc)
                .FirstOrDefaultAsync(cancellationToken);

            string normalized = pass.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            if (stored is null
                || stored.ExpiresUtc <= now
                || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(stored.SecretHash), Convert.FromBase64String(Hash(user.Id, FormatPass(normalized)))))
            {
                await auditLogger.LogAsync(
                    new AuditEventEntry { Action = AuditActions.TapFailed, Subject = user.Id, Outcome = "failure" },
                    cancellationToken);
                return null;
            }

            int consumed = await context.Set<TemporaryAccessPass>()
                .Where(p => p.Id == stored.Id && p.ConsumedUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(static p => p.ConsumedUtc, now), cancellationToken);
            if (consumed != 1)
            {
                return null;
            }

            await auditLogger.LogAsync(
                new AuditEventEntry { Action = AuditActions.TapUsed, Subject = user.Id, Outcome = "success" },
                cancellationToken);
            return user.Id;
        }

        /// <summary>Groups a raw pass into readable four-character segments.</summary>
        private static string FormatPass(string raw)
        {
            StringBuilder builder = new(raw.Length + (raw.Length / 4));
            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    builder.Append('-');
                }

                builder.Append(raw[i]);
            }

            return builder.ToString();
        }

        /// <summary>Computes the stored hash of a pass, bound to its user.</summary>
        private static string Hash(string userId, string pass)
        {
            string normalized = pass.Replace("-", string.Empty, StringComparison.Ordinal).ToUpper(CultureInfo.InvariantCulture);
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(userId + ":" + normalized)));
        }
    }
}
