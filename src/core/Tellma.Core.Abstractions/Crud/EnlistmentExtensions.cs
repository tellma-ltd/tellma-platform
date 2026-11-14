// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Enlists writes into another stack with the current frame: the target stack's checks,
    ///     validators and effects run as participants of the host, its statements as one more group in
    ///     the host's persist batch, and nothing opens a second transaction.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The receiver is the writer, and its <see cref="IEnlists{TTarget}" /> declaration is the
    ///         compile-time gate. The target's stack must accept enlisted writes, and the writer must be
    ///         a participant of the host's frame.
    ///     </para>
    ///     <para>
    ///         A pipeline host admits enlistment while it is composing (from its validation hooks, an
    ///         entity action's method and its validator components), never from preprocessing, a
    ///         persist effect or post-commit work. An open host (an API action, a job or a provisioning
    ///         step) admits it until it persists. Every refusal is an
    ///         <see cref="InvalidOperationException" />: an authoring bug, never a user outcome.
    ///     </para>
    /// </remarks>
    public static class EnlistmentExtensions
    {
        /// <summary>
        ///     Enlists a save of rows of the target stack. The target's shape checks, preprocessing, id
        ///     assignment, before images and validators run as tasks on the host's validation scheduler;
        ///     the returned task completes once they did, with the rows carrying their final ids.
        /// </summary>
        /// <typeparam name="TTarget">The target stack's entity type.</typeparam>
        /// <param name="writer">The writer, which declares the pair.</param>
        /// <param name="host">The frame to enlist with, a context's host or an injected open frame.</param>
        /// <param name="rows">The rows to save.</param>
        /// <param name="options">How the save runs; null for the defaults.</param>
        /// <returns>The enlisted save.</returns>
        /// <exception cref="ArgumentNullException">An argument but <paramref name="options" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The pair is undeclared, the writer is not a participant, the host does not admit enlistment now, or the host is not a platform frame.</exception>
        public static Task<EnlistedSave<TTarget>> EnlistSaveAsync<TTarget>(
            this IEnlists<TTarget> writer, IWriteHost host, IReadOnlyList<TTarget> rows, EnlistSaveOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(rows);
            return Frame(host).EnlistSaveAsync(writer, rows, options ?? new EnlistSaveOptions());
        }

        /// <summary>
        ///     Enlists a delete of rows of the target stack. The target's delete validators run over the
        ///     target rows, loaded unfiltered in the current round; the rows are deleted when the host
        ///     commits.
        /// </summary>
        /// <typeparam name="TTarget">The target stack's entity type.</typeparam>
        /// <typeparam name="TKey">The target stack's key type.</typeparam>
        /// <param name="writer">The writer, which declares the pair.</param>
        /// <param name="host">The frame to enlist with, a context's host or an injected open frame.</param>
        /// <param name="ids">The ids of the rows to delete.</param>
        /// <param name="options">How the delete runs; null for the defaults.</param>
        /// <returns>The enlisted delete.</returns>
        /// <exception cref="ArgumentNullException">An argument but <paramref name="options" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The pair is undeclared, the writer is not a participant, the host does not admit enlistment now, or the host is not a platform frame.</exception>
        public static EnlistedDelete<TKey> EnlistDelete<TTarget, TKey>(
            this IEnlists<TTarget> writer, IWriteHost host, IReadOnlyList<TKey> ids, EnlistDeleteOptions? options = null)
            where TTarget : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(ids);
            return Frame(host).EnlistDelete(writer, ids, options ?? new EnlistDeleteOptions());
        }

        /// <summary>The platform frame behind a host.</summary>
        /// <param name="host">The host.</param>
        /// <returns>The frame.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="host" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The host is not one the platform opened.</exception>
        private static IEnlistmentHost Frame(IWriteHost host)
        {
            ArgumentNullException.ThrowIfNull(host);
            return host as IEnlistmentHost ?? throw new InvalidOperationException(
                $"Writes enlist only with a frame the platform opened: a context's Host or an injected {nameof(IOpenWriteHost)}. The host '{host.Operation}' is not one.");
        }
    }

    /// <summary>The platform's frames: the enlistment entry points the extension methods forward to.</summary>
    internal interface IEnlistmentHost
    {
        /// <summary>Enlists a save with the frame.</summary>
        /// <typeparam name="TTarget">The target stack's entity type.</typeparam>
        /// <param name="writer">The writer, which declares the pair.</param>
        /// <param name="rows">The rows to save.</param>
        /// <param name="options">How the save runs.</param>
        /// <returns>The enlisted save, once the target's checks and validators completed.</returns>
        Task<EnlistedSave<TTarget>> EnlistSaveAsync<TTarget>(IEnlists<TTarget> writer, IReadOnlyList<TTarget> rows, EnlistSaveOptions options);

        /// <summary>Enlists a delete with the frame.</summary>
        /// <typeparam name="TTarget">The target stack's entity type.</typeparam>
        /// <typeparam name="TKey">The target stack's key type.</typeparam>
        /// <param name="writer">The writer, which declares the pair.</param>
        /// <param name="ids">The ids of the rows to delete.</param>
        /// <param name="options">How the delete runs.</param>
        /// <returns>The enlisted delete.</returns>
        EnlistedDelete<TKey> EnlistDelete<TTarget, TKey>(IEnlists<TTarget> writer, IReadOnlyList<TKey> ids, EnlistDeleteOptions options)
            where TTarget : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>;
    }
}
