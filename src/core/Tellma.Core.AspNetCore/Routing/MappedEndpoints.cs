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
    /// <remarks>
    ///     Until the gate seals them, each read builds every endpoint afresh, because a distribution
    ///     may still be mapping. Sealing wraps each source so that its endpoints are built once, and
    ///     fixes the set of sources: the audit, the authorization policy cache, the router and the
    ///     route-miss index then read the same endpoints, so the endpoints the gate audited are the
    ///     ones the host serves. The one exception is a source that signals a change, such as the
    ///     in-proc identity server's controllers and pages under hot reload: it is read again after
    ///     it does, and what it then adds is served without being audited.
    /// </remarks>
    internal sealed class MappedEndpoints
    {
        /// <summary>Guards the sealing.</summary>
        private readonly Lock _gate = new();

        /// <summary>The application's endpoint sources; null until <c>MapTellma</c> captures them.</summary>
        private ICollection<EndpointDataSource>? _sources;

        /// <summary>The sealed sources, in order; null until the gate seals them.</summary>
        private SealedEndpointDataSource[]? _sealed;

        /// <summary>Whether <c>MapTellma</c> has run.</summary>
        public bool IsMapped => Volatile.Read(ref _sources) is not null;

        /// <summary>
        ///     Every endpoint of the captured sources: built afresh from the sources as they stand
        ///     until they are sealed, and from the sealed sources alone after, so a source added later
        ///     is never read.
        /// </summary>
        /// <returns>The endpoints.</returns>
        /// <exception cref="InvalidOperationException"><c>MapTellma</c> has not run.</exception>
        public IReadOnlyList<Endpoint> Endpoints()
        {
            ICollection<EndpointDataSource> sources = Volatile.Read(ref _sources)
                ?? throw new InvalidOperationException("MapTellma has not mapped the application's endpoints.");

            // The router copied the sealed sources when the pipeline was built, so once sealed this
            // reads exactly those, whatever the application's collection gained since.
            IEnumerable<EndpointDataSource> read = Volatile.Read(ref _sealed) ?? sources;
            return [.. read.SelectMany(static source => source.Endpoints)];
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

        /// <summary>
        ///     Seals the captured sources, once: each is replaced, in place and in order, by a wrapper
        ///     that builds its endpoints on the first read and keeps them for every later one. The host
        ///     seals as it starts, when nothing maps any more; an endpoint mapped after that, in a sealed
        ///     group or in a source of its own, is neither audited nor served.
        /// </summary>
        /// <exception cref="InvalidOperationException"><c>MapTellma</c> has not run.</exception>
        public void Seal()
        {
            ICollection<EndpointDataSource> sources = Volatile.Read(ref _sources)
                ?? throw new InvalidOperationException("MapTellma has not mapped the application's endpoints.");
            lock (_gate)
            {
                if (_sealed is not null)
                {
                    return;
                }

                // --- Each source behind a wrapper that keeps its endpoints, the order unchanged ---
                // The router and the global endpoint list are built from this same collection once
                // the host has started, so they read the wrappers too.
                SealedEndpointDataSource[] wrapped = [.. sources.Select(static source => source as SealedEndpointDataSource ?? new SealedEndpointDataSource(source))];
                sources.Clear();
                foreach (SealedEndpointDataSource source in wrapped)
                {
                    sources.Add(source);
                }

                // --- The snapshot every later read of this class answers from ---
                Volatile.Write(ref _sealed, wrapped);
            }
        }
    }
}
