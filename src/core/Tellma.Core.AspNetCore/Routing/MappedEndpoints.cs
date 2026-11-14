// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>
    ///     The application's endpoint sources, captured by <c>MapTellma</c>, so the realised gate's
    ///     audit walks every endpoint the host maps, a distribution's mapped after <c>MapTellma</c>
    ///     returns included.
    /// </summary>
    internal sealed class MappedEndpoints
    {
        private ICollection<EndpointDataSource>? _sources;

        /// <summary>Whether <c>MapTellma</c> has run.</summary>
        public bool IsMapped => Volatile.Read(ref _sources) is not null;

        /// <summary>Every endpoint of the captured sources, as they stand now.</summary>
        /// <returns>The endpoints.</returns>
        /// <exception cref="InvalidOperationException"><c>MapTellma</c> has not run.</exception>
        public IReadOnlyList<Endpoint> Endpoints()
        {
            ICollection<EndpointDataSource> sources = Volatile.Read(ref _sources)
                ?? throw new InvalidOperationException("MapTellma has not mapped the application's endpoints.");
            return [.. sources.SelectMany(static source => source.Endpoints)];
        }

        /// <summary>Captures the application's sources; once.</summary>
        /// <param name="sources">The application's endpoint sources.</param>
        /// <exception cref="InvalidOperationException">They were already captured: <c>MapTellma</c> ran twice.</exception>
        public void Capture(ICollection<EndpointDataSource> sources)
        {
            ArgumentNullException.ThrowIfNull(sources);
            if (Interlocked.CompareExchange(ref _sources, sources, null) is not null)
            {
                throw new InvalidOperationException("MapTellma maps the platform's endpoints once; it was called again.");
            }
        }
    }
}
