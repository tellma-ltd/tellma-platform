// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Client ID metadata documents: clients whose <c>client_id</c> is the <c>https</c> URL of
    ///     their own metadata document, which the engine fetches, validates and keeps as an
    ///     ordinary application row. Bound from <c>TellmaIdentity:ClientMetadata</c>.
    /// </summary>
    public sealed class TellmaIdentityClientMetadataOptions
    {
        /// <summary>
        ///     Accept document clients. When off, a <c>client_id</c> that is an <c>https</c> URL is
        ///     refused with <c>invalid_client</c> and discovery no longer advertises the feature; a
        ///     stored document-client row is never used without its document.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The largest document or key-set body accepted, in bytes.</summary>
        public int MaxBytes { get; set; } = 5120;

        /// <summary>How long one fetch may take, from resolving the host to reading the last byte.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        ///     The shortest time a fetched document is cached, whatever its HTTP caching headers say,
        ///     so a document that forbids caching is not fetched on every request.
        /// </summary>
        public TimeSpan MinCache { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>The longest time a fetched document is cached, whatever its HTTP caching headers say.</summary>
        public TimeSpan MaxCache { get; set; } = TimeSpan.FromHours(24);

        /// <summary>
        ///     How many fetches one host may be sent per minute, documents and key sets together, in
        ///     each of two budgets: one for re-fetching the documents of clients the engine already
        ///     holds, one for every other fetch, so fetches anyone can cause never starve the
        ///     clients already in use. A fetch past the limit is not sent.
        /// </summary>
        public int FetchesPerHostPerMinute { get; set; } = 10;

        /// <summary>
        ///     How many documents may be resolved at once across all hosts, in each of the same two
        ///     budgets. A first-time or early fetch that finds no free slot is refused; a refresh of
        ///     a known client waits for one up to <see cref="Timeout" />.
        /// </summary>
        public int MaxConcurrentFetches { get; set; } = 16;

        /// <summary>
        ///     The most document-client rows the store holds. At the cap a new document client is
        ///     refused with <c>invalid_client</c>; the clients already stored keep working.
        /// </summary>
        public int MaxClients { get; set; } = 10000;

        /// <summary>
        ///     How long a document-client row that holds no authorization is kept after it was
        ///     created; the hourly prune deletes it after that.
        /// </summary>
        public TimeSpan UnusedClientLifetime { get; set; } = TimeSpan.FromHours(1);
    }
}
