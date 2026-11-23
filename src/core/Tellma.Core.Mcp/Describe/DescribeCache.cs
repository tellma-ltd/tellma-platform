// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Caching.Memory;
using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Mcp.Describe
{
    /// <summary>
    ///     The entity descriptions <c>tellma_describe</c> has rendered, per tenant, entity and
    ///     language, each valid while the tenant's settings tag is the one it was rendered under: a
    ///     settings change, the multilingual shape among them, renders it afresh.
    /// </summary>
    internal sealed class DescribeCache : IDisposable
    {
        /// <summary>The descriptions held at most, across tenants.</summary>
        public const int Capacity = 2048;

        /// <summary>How long a description unread is kept.</summary>
        public static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(30);

        /// <summary>The descriptions, each with the settings tag it was rendered under.</summary>
        private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = Capacity });

        /// <summary>A description, rendering and keeping it when none is held under the current settings tag.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="entity">The entity's name.</param>
        /// <param name="culture">The language the description's labels are in.</param>
        /// <param name="tag">The tenant's current settings tag.</param>
        /// <param name="render">Renders the description.</param>
        /// <returns>The description.</returns>
        public string GetOrRender(int tenantId, string entity, string culture, VersionTag tag, Func<string> render)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(culture);
            ArgumentNullException.ThrowIfNull(render);
            (int, string, string) key = (tenantId, entity, culture);
            if (_cache.TryGetValue(key, out Entry? held) && held is not null && EqualityComparer<VersionTag>.Default.Equals(held.Tag, tag))
            {
                return held.Text;
            }

            // A render is pure and cheap next to its read, so two concurrent misses both rendering is harmless.
            string text = render();
            _ = _cache.Set(key, new Entry(tag, text), new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = SlidingExpiration });
            return text;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cache.Dispose();
        }

        /// <summary>A held description.</summary>
        /// <param name="Tag">The settings tag it was rendered under.</param>
        /// <param name="Text">The description.</param>
        private sealed record Entry(VersionTag Tag, string Text);
    }
}
