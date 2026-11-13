// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore
{
    /// <summary>
    ///     The constraints a sign-in must meet, which the BFF forwards to the identity server with the
    ///     challenge. The platform's default imposes none; a distribution registers its own to require
    ///     an assurance level or restrict the sign-in methods.
    /// </summary>
    public interface IAuthenticationPolicyProvider
    {
        /// <summary>The constraints on a sign-in.</summary>
        /// <param name="tenantId">The tenant the sign-in is for; null for the initial sign-in, which has no tenant yet.</param>
        /// <param name="cancellationToken">Cancels the lookup.</param>
        /// <returns>The policy.</returns>
        Task<LoginPolicy> GetLoginPolicyAsync(int? tenantId, CancellationToken cancellationToken = default);
    }

    /// <summary>The constraints on a sign-in.</summary>
    /// <param name="AcrValues">The authentication context classes the sign-in must reach, space-separated; null for none.</param>
    /// <param name="MaxAge">How recently the user must have authenticated; null for no limit.</param>
    /// <param name="AllowedMethods">The sign-in methods the identity server may offer; null for all of them.</param>
    public sealed record LoginPolicy(string? AcrValues, TimeSpan? MaxAge, IReadOnlyList<string>? AllowedMethods)
    {
        /// <summary>No constraint at all.</summary>
        public static LoginPolicy None { get; } = new(null, null, null);
    }

    /// <summary>The platform's login policy: no constraint on any sign-in.</summary>
    internal sealed class DefaultAuthenticationPolicyProvider : IAuthenticationPolicyProvider
    {
        /// <inheritdoc />
        public Task<LoginPolicy> GetLoginPolicyAsync(int? tenantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LoginPolicy.None);
        }
    }
}
