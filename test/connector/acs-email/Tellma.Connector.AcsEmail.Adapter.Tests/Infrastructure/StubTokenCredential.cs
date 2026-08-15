// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Infrastructure
{
    /// <summary>
    ///     A credential that hands back a fixed token, so the pipeline's auth policy has something to
    ///     attach without any of the suites reaching for a real identity.
    /// </summary>
    internal sealed class StubTokenCredential : TokenCredential
    {
        /// <inheritdoc />
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new AccessToken("stub-token", DateTimeOffset.UtcNow.AddHours(1));
        }

        /// <inheritdoc />
        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }
    }
}
