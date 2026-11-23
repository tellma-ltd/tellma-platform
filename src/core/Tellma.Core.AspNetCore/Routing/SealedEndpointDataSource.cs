// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>
    ///     An endpoint source that builds its inner source's endpoints once and answers every later
    ///     read with the same instances, until the inner source signals a change.
    /// </summary>
    /// <remarks>
    ///     A minimal-API source builds every endpoint afresh on each read: it runs each route's
    ///     conventions again and compiles a new request delegate for each handler, and it never
    ///     signals a change. The host reads its endpoints several times as it starts (the startup
    ///     audit, the authorization policy cache, the router's matcher, the route-miss index), so
    ///     without this each read paid for the whole table again. A source that does signal a
    ///     change, such as the identity server's controllers and pages, is read again after its
    ///     change token fires.
    /// </remarks>
    /// <param name="inner">The source whose endpoints are kept.</param>
    internal sealed class SealedEndpointDataSource(EndpointDataSource inner) : EndpointDataSource, IDisposable
    {
        /// <summary>Guards the kept endpoints and the token they were read under.</summary>
        private readonly Lock _gate = new();

        /// <summary>The endpoints of the last read; null before the first.</summary>
        private IReadOnlyList<Endpoint>? _endpoints;

        /// <summary>The inner source's change token, taken just before the last read.</summary>
        private IChangeToken? _readUnder;

        /// <summary>The source whose endpoints are kept.</summary>
        public EndpointDataSource Inner => inner;

        /// <inheritdoc />
        public override IReadOnlyList<Endpoint> Endpoints
        {
            get
            {
                lock (_gate)
                {
                    // The token is taken before the read, so a change that lands while the
                    // endpoints build leaves it fired and the next read builds them again.
                    if (_endpoints is null || _readUnder is null || _readUnder.HasChanged)
                    {
                        _readUnder = inner.GetChangeToken();
                        _endpoints = inner.Endpoints;
                    }

                    return _endpoints;
                }
            }
        }

        /// <inheritdoc />
        public override IChangeToken GetChangeToken()
        {
            return inner.GetChangeToken();
        }

        /// <inheritdoc />
        public override IReadOnlyList<Endpoint> GetGroupedEndpoints(RouteGroupContext context)
        {
            // A group's prefix and conventions make other endpoints than the source's own, so they
            // are built as the inner source builds them, never from the kept ones.
            return inner.GetGroupedEndpoints(context);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // The composites that read a source dispose it; they now hold this wrapper instead.
            (inner as IDisposable)?.Dispose();
        }
    }
}
