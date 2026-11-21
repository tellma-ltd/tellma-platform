// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Testing.Blobs
{
    /// <summary>
    ///     An <see cref="IBlobStore" /> that keeps every object in memory, for test hosts that stage
    ///     or serve blobs without touching a disk or an emulator. It passes the blob-store
    ///     conformance suite, so code tested over it behaves as it does over a real store.
    /// </summary>
    /// <remarks>
    ///     Thread-safe. Writes are atomic per object, so it leaves no incomplete artefacts and the
    ///     purge stores nothing; <see cref="Purges" /> records each purge's cut-off.
    ///     <see cref="FailWrites" /> makes every later write throw and <see cref="FailDeletes" /> keeps
    ///     the objects it matches, for testing what a caller does when the store fails.
    /// </remarks>
    public sealed class InMemoryBlobStore : IBlobStore
    {
        // Each tenant's objects by name; a tenant without an entry has no storage.
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, StoredObject>> _tenants = new();

        // Each purge's tenant and cut-off, in call order.
        private readonly ConcurrentQueue<(int TenantId, DateTimeOffset OlderThan)> _purges = new();

        /// <summary>
        ///     The failure every later <see cref="WriteAsync" /> throws after validating its arguments
        ///     and before storing anything; null (the default) for none.
        /// </summary>
        public Exception? FailWrites { get; set; }

        /// <summary>
        ///     The names every later <see cref="DeleteAsync" /> fails to delete: the call deletes the
        ///     others, then throws a <see cref="BlobStoreException" /> when it kept any; null (the
        ///     default) for none.
        /// </summary>
        public Func<string, bool>? FailDeletes { get; set; }

        /// <summary>Each <see cref="PurgeIncompleteAsync" /> call's tenant and cut-off, in call order.</summary>
        public IReadOnlyList<(int TenantId, DateTimeOffset OlderThan)> Purges => [.. _purges];

        /// <summary>Forgets every purge <see cref="Purges" /> recorded, for a test that reuses the store.</summary>
        public void ClearPurges()
        {
            _purges.Clear();
        }

        /// <summary>Whether the tenant's storage exists.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>True when it exists.</returns>
        public bool TenantExists(int tenantId)
        {
            return _tenants.ContainsKey(tenantId);
        }

        /// <summary>Removes the tenant's storage and everything in it, behind the store's back.</summary>
        /// <param name="tenantId">The tenant.</param>
        public void RemoveTenant(int tenantId)
        {
            _ = _tenants.TryRemove(tenantId, out _);
        }

        /// <summary>The names of the tenant's objects, in ordinal order.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The names; empty when the tenant has no storage.</returns>
        public IReadOnlyList<string> Names(int tenantId)
        {
            return _tenants.TryGetValue(tenantId, out ConcurrentDictionary<string, StoredObject>? objects)
                ? [.. objects.Keys.Order(StringComparer.Ordinal)]
                : [];
        }

        /// <summary>One object's bytes and media type, or null when it does not exist.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="name">The object's name.</param>
        /// <returns>The bytes and media type, or null.</returns>
        public (byte[] Bytes, string ContentType)? Find(int tenantId, string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return _tenants.TryGetValue(tenantId, out ConcurrentDictionary<string, StoredObject>? objects)
                && objects.TryGetValue(name, out StoredObject? stored)
                    ? (stored.Bytes.ToArray(), stored.ContentType)
                    : null;
        }

        /// <inheritdoc />
        public Task EnsureTenantAsync(int tenantId, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
            _ = Tenant(tenantId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task WriteAsync(int tenantId, IReadOnlyList<BlobWrite> writes, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
            ArgumentNullException.ThrowIfNull(writes);

            // --- Every argument, before anything is stored ---
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (BlobWrite? write in writes)
            {
                ArgumentNullException.ThrowIfNull(write, nameof(writes));
                if (!BlobName.IsValid(write.Name))
                {
                    throw new ArgumentException($"'{write.Name}' is not a valid blob name.", nameof(writes));
                }

                if (write.Length < 0)
                {
                    throw new ArgumentException($"The blob write '{write.Name}' declares a negative length.", nameof(writes));
                }

                if (!names.Add(write.Name))
                {
                    throw new ArgumentException($"The blob '{write.Name}' appears twice in one write.", nameof(writes));
                }
            }

            if (writes.Count == 0)
            {
                return;
            }

            if (FailWrites is Exception failure)
            {
                throw failure;
            }

            // --- Each object in list order, best-effort: the first failure is thrown after the rest ---
            ConcurrentDictionary<string, StoredObject> objects = Tenant(tenantId);
            Exception? first = null;
            foreach (BlobWrite write in writes)
            {
                try
                {
                    byte[] bytes = await ReadExactlyAsync(write, cancellationToken).ConfigureAwait(false);
                    if (!objects.TryAdd(write.Name, new StoredObject(bytes, write.ContentType, DateTimeOffset.UtcNow)))
                    {
                        throw new BlobAlreadyExistsException(write.Name);
                    }
                }
                catch (Exception exception) when (exception is BlobAlreadyExistsException or BlobStoreException)
                {
                    first ??= exception;
                }
            }

            if (first is not null)
            {
                throw first;
            }
        }

        /// <inheritdoc />
        public Task<BlobContent?> OpenReadAsync(int tenantId, string name, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
            ArgumentNullException.ThrowIfNull(name);
            if (!BlobName.IsValid(name))
            {
                throw new ArgumentException($"'{name}' is not a valid blob name.", nameof(name));
            }

            BlobContent? content = _tenants.TryGetValue(tenantId, out ConcurrentDictionary<string, StoredObject>? objects)
                && objects.TryGetValue(name, out StoredObject? stored)
                    ? new BlobContent(new MemoryStream(stored.Bytes, writable: false), stored.Bytes.LongLength)
                    : null;
            return Task.FromResult(content);
        }

        /// <inheritdoc />
        public Task DeleteAsync(int tenantId, IReadOnlyList<string> names, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
            ArgumentNullException.ThrowIfNull(names);
            foreach (string? name in names)
            {
                ArgumentNullException.ThrowIfNull(name, nameof(names));
                if (!BlobName.IsValid(name))
                {
                    throw new ArgumentException($"'{name}' is not a valid blob name.", nameof(names));
                }
            }

            // --- Each object but the failing ones, then the failure when any was kept ---
            Func<string, bool>? fail = FailDeletes;
            List<string> kept = [.. names.Where(name => fail?.Invoke(name) == true)];
            if (_tenants.TryGetValue(tenantId, out ConcurrentDictionary<string, StoredObject>? objects))
            {
                foreach (string name in names.Except(kept, StringComparer.Ordinal))
                {
                    _ = objects.TryRemove(name, out _);
                }
            }

            return kept.Count == 0
                ? Task.CompletedTask
                : Task.FromException(new BlobStoreException($"Deleting {kept.Count} blob objects failed.", null));
        }

        /// <inheritdoc />
        public IAsyncEnumerable<BlobStoreEntry> ListAsync(int tenantId, string prefix, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
            ArgumentNullException.ThrowIfNull(prefix);
            return BlobName.IsValidPrefix(prefix)
                ? ListCoreAsync(tenantId, prefix + "/", cancellationToken)
                : throw new ArgumentException($"'{prefix}' is not a valid blob listing prefix: a kind, or a kind and a fan-out segment.", nameof(prefix));
        }

        /// <inheritdoc />
        public Task PurgeIncompleteAsync(int tenantId, DateTimeOffset olderThan, CancellationToken cancellationToken)
        {
            // Writes are atomic per object, so nothing incomplete is ever left; the call is recorded.
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
            _purges.Enqueue((tenantId, olderThan));
            return Task.CompletedTask;
        }

        /// <summary>The tenant's objects, its storage created when missing.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The objects.</returns>
        private ConcurrentDictionary<string, StoredObject> Tenant(int tenantId)
        {
            return _tenants.GetOrAdd(tenantId, static _ => new ConcurrentDictionary<string, StoredObject>(StringComparer.Ordinal));
        }

        /// <summary>Snapshots the objects under a prefix and yields them in name order.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="boundary">The prefix with its trailing separator, so whole segments only match.</param>
        /// <param name="cancellationToken">Cancels the enumeration.</param>
        /// <returns>The entries.</returns>
        private async IAsyncEnumerable<BlobStoreEntry> ListCoreAsync(
            int tenantId, string boundary, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!_tenants.TryGetValue(tenantId, out ConcurrentDictionary<string, StoredObject>? objects))
            {
                yield break;
            }

            foreach (KeyValuePair<string, StoredObject> entry in objects
                .Where(entry => entry.Key.StartsWith(boundary, StringComparison.Ordinal))
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new BlobStoreEntry(entry.Key, entry.Value.Bytes.LongLength, entry.Value.LastModified);
                await Task.Yield();
            }
        }

        /// <summary>Reads a write's content, which must yield exactly its declared length.</summary>
        /// <param name="write">The write.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The bytes.</returns>
        /// <exception cref="BlobStoreException">The content yields fewer or more bytes than declared.</exception>
        private static async Task<byte[]> ReadExactlyAsync(BlobWrite write, CancellationToken cancellationToken)
        {
            using MemoryStream buffer = new();
            await write.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.Length == write.Length
                ? buffer.ToArray()
                : throw new BlobStoreException(
                    $"The content of the blob write '{write.Name}' yielded {buffer.Length} bytes, not the {write.Length} it declared.", null);
        }

        /// <summary>One stored object.</summary>
        /// <param name="Bytes">Its bytes.</param>
        /// <param name="ContentType">Its media type.</param>
        /// <param name="LastModified">When it was written, in UTC.</param>
        private sealed record StoredObject(byte[] Bytes, string ContentType, DateTimeOffset LastModified);
    }
}
