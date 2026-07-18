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
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Email;
using Tellma.Identity.Services.RateLimiting;

namespace Tellma.Identity.Services.EmailCodes
{
    /// <summary>The database-backed single-use email code service.</summary>
    /// <param name="context">The identity store.</param>
    /// <param name="userManager">The Identity user manager (email lookup).</param>
    /// <param name="rateLimits">Issuance rate limiting.</param>
    /// <param name="emailQueue">The background mail dispatch queue.</param>
    /// <param name="templates">Localized message construction.</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="metrics">Identity metrics.</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class EmailCodeService(
        TellmaIdentityDbContext context,
        UserManager<TellmaIdentityUser> userManager,
        IRateLimitCounterStore rateLimits,
        IEmailDispatcher emailQueue,
        EmailTemplateService templates,
        IAuditLogger auditLogger,
        IdentityMetrics metrics,
        TimeProvider timeProvider) : IEmailCodeService
    {
        /// <summary>Failed attempts before the code is invalidated.</summary>
        public const int MaxAttempts = 5;

        /// <summary>Codes per user per hour before issuance is suppressed.</summary>
        public const int MaxCodesPerUserPerHour = 5;

        /// <summary>Codes per IP per hour before issuance is suppressed.</summary>
        public const int MaxCodesPerIpPerHour = 10;

        /// <summary>The code lifetime.</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        /// <inheritdoc />
        public async Task RequestCodeAsync(
            string email,
            SingleUseCodePurpose purpose,
            string flowBinding,
            string? ipAddress,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(email);
            ArgumentException.ThrowIfNullOrWhiteSpace(flowBinding);

            // Rate-limit by IP before any user lookup: an unknown address is counted too, so an
            // attacker cannot probe unlimited addresses, and the miss path does the same work.
            var window = TimeSpan.FromHours(1);
            int perIp = ipAddress is null
                ? 0
                : await rateLimits.IncrementAsync($"emailcode:ip:{ipAddress}", window, cancellationToken);

            TellmaIdentityUser? user = await userManager.FindByEmailAsync(email);
            if (perIp > MaxCodesPerIpPerHour || user is null || user.LifecycleState != UserLifecycleState.Active)
            {
                if (perIp > MaxCodesPerIpPerHour)
                {
                    await auditLogger.LogAsync(
                        new AuditEventEntry
                        {
                            Action = AuditActions.EmailCodeRateLimited,
                            Subject = user?.Id,
                            IpAddress = ipAddress,
                            Outcome = "failure",
                        },
                        cancellationToken);
                }

                return;
            }

            int perUser = await rateLimits.IncrementAsync($"emailcode:user:{user.Id}", window, cancellationToken);
            if (perUser > MaxCodesPerUserPerHour)
            {
                await auditLogger.LogAsync(
                    new AuditEventEntry
                    {
                        Action = AuditActions.EmailCodeRateLimited,
                        Subject = user.Id,
                        IpAddress = ipAddress,
                        Outcome = "failure",
                    },
                    cancellationToken);
                return;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();

            // Issuing a new code retires any outstanding one for the same purpose.
            await context.Set<SingleUseCode>()
                .Where(c => c.UserId == user.Id && c.Purpose == purpose && c.ConsumedUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(static c => c.ConsumedUtc, now), cancellationToken);

            string code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", CultureInfo.InvariantCulture);
            context.Set<SingleUseCode>().Add(new SingleUseCode
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = user.Id,
                Purpose = purpose,
                SecretHash = Hash(user.Id, code),
                FlowBinding = flowBinding,
                CreatedUtc = now,
                ExpiresUtc = now.Add(Lifetime),
            });
            await context.SaveChangesAsync(cancellationToken);

            // Hand delivery to the background worker so the request returns without an SMTP wait.
            emailQueue.Enqueue([templates.SignInCode(user, code)]);

            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.EmailCodeIssued,
                    Subject = user.Id,
                    IpAddress = ipAddress,
                    Outcome = "success",
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { purpose = purpose.ToString() }),
                },
                cancellationToken);

            metrics.EmailCode(purpose.ToString(), "issued");
        }

        /// <inheritdoc />
        public async Task<EmailCodeVerificationResult> VerifyAsync(
            TellmaIdentityUser user,
            SingleUseCodePurpose purpose,
            string? flowBinding,
            string code,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            DateTimeOffset now = timeProvider.GetUtcNow();

            SingleUseCode? outstanding = await context.Set<SingleUseCode>()
                .Where(c => c.UserId == user.Id && c.Purpose == purpose && c.ConsumedUtc == null)
                .OrderByDescending(static c => c.CreatedUtc)
                .FirstOrDefaultAsync(cancellationToken);

            EmailCodeVerificationResult result = Check(outstanding, user, flowBinding, code, now);

            if (result == EmailCodeVerificationResult.Invalid && outstanding is not null)
            {
                // Count the failed attempt; past the maximum the code is dead.
                await context.Set<SingleUseCode>()
                    .Where(c => c.Id == outstanding.Id)
                    .ExecuteUpdateAsync(
                        static setters => setters.SetProperty(static c => c.Attempts, static c => c.Attempts + 1),
                        cancellationToken);
            }
            else if (result == EmailCodeVerificationResult.Success)
            {
                // Single-use under concurrency: the conditional update has exactly one winner.
                int consumed = await context.Set<SingleUseCode>()
                    .Where(c => c.Id == outstanding!.Id && c.ConsumedUtc == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(static c => c.ConsumedUtc, now), cancellationToken);
                if (consumed != 1)
                {
                    result = EmailCodeVerificationResult.Invalid;
                }
            }

            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = result == EmailCodeVerificationResult.Success
                        ? AuditActions.EmailCodeVerified
                        : AuditActions.EmailCodeFailed,
                    Subject = user.Id,
                    Outcome = result == EmailCodeVerificationResult.Success ? "success" : "failure",
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(
                        new { purpose = purpose.ToString(), result = result.ToString() }),
                },
                cancellationToken);

            metrics.EmailCode(purpose.ToString(), result == EmailCodeVerificationResult.Success ? "verified" : "failed");
            return result;
        }

        /// <summary>Computes the stored hash of a code, bound to its user.</summary>
        internal static string Hash(string userId, string secret)
        {
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(userId + ":" + secret)));
        }

        /// <summary>Applies the verification rules to the outstanding code.</summary>
        private static EmailCodeVerificationResult Check(
            SingleUseCode? outstanding, TellmaIdentityUser user, string? flowBinding, string code, DateTimeOffset now)
        {
            if (outstanding is null)
            {
                return EmailCodeVerificationResult.Invalid;
            }

            if (outstanding.ExpiresUtc <= now)
            {
                return EmailCodeVerificationResult.Expired;
            }

            if (outstanding.Attempts >= MaxAttempts)
            {
                return EmailCodeVerificationResult.TooManyAttempts;
            }

            // Session binding: the code only verifies in the browser flow that requested it.
            if (outstanding.FlowBinding is not null
                && !string.Equals(outstanding.FlowBinding, flowBinding, StringComparison.Ordinal))
            {
                return EmailCodeVerificationResult.Invalid;
            }

            byte[] expected = Convert.FromBase64String(outstanding.SecretHash);
            byte[] actual = Convert.FromBase64String(Hash(user.Id, code));
            return CryptographicOperations.FixedTimeEquals(expected, actual)
                ? EmailCodeVerificationResult.Success
                : EmailCodeVerificationResult.Invalid;
        }
    }
}
