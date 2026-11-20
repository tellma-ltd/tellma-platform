// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Testing.Crud
{
    /// <summary>
    ///     The contexts a stack service's validation hooks see, as the pipeline hands them over after its
    ///     first round: a save's payload with which rows are new and their before images, a delete's
    ///     ids and loaded targets. A service's or a validator's unit test drives its rules through
    ///     them with a scripted <see cref="IContextLoader" />, and no database.
    /// </summary>
    /// <remarks>
    ///     The contexts carry what the first round produces from the start, so a rule may read a
    ///     before image or a target whether or not it awaited <see cref="IContextLoader.LoadAsync" />
    ///     first; the loader the test supplies decides what its loads answer. The write host refuses
    ///     enlistment and confirmations, which a rule under test does not reach.
    /// </remarks>
    public static class ValidationContexts
    {
        /// <summary>A save's context after its first round.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <param name="loader">The loader the rules read context through.</param>
        /// <param name="context">Who is saving.</param>
        /// <param name="rows">The payload in order, each with its before image; a null image marks a new row.</param>
        /// <param name="options">The save's options; the defaults when null.</param>
        /// <returns>The context, with an empty error list.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static SaveContext<TEntity> Save<TEntity>(
            IContextLoader loader, RequestContext context, IReadOnlyList<(TEntity Row, TEntity? Before)> rows, SaveOptions? options = null)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(loader);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(rows);
            return new SaveContext<TEntity>
            {
                Entities = [.. rows.Select(static row => row.Row)],
                Options = options ?? new SaveOptions(),
                Context = context,
                Loader = loader,
                Errors = new ValidationErrors<TEntity>(),
                Host = new RefusingWriteHost(AccessOperation.Save),
                NewRows = [.. rows.Select(static row => row.Before is null)],
                BeforeImages = [.. rows.Select(static row => row.Before)],
            };
        }

        /// <summary>A delete's context after its first round.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="loader">The loader the rules read context through.</param>
        /// <param name="context">Who is deleting.</param>
        /// <param name="ids">The ids the delete names, in request order.</param>
        /// <param name="targets">The targets the first round loaded, in id order.</param>
        /// <param name="withDescendants">Whether the delete removes each target's descendants with it.</param>
        /// <returns>The context, with an empty error list.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static DeleteContext<TEntity, TKey> Delete<TEntity, TKey>(
            IContextLoader loader, RequestContext context, IReadOnlyList<TKey> ids, IReadOnlyList<TEntity> targets, bool withDescendants = false)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(loader);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(targets);
            return new DeleteContext<TEntity, TKey>
            {
                Ids = ids,
                Context = context,
                Loader = loader,
                Errors = new ValidationErrors<TEntity>(),
                Host = new RefusingWriteHost(AccessOperation.Delete),
                WithDescendants = withDescendants,
                Entities = targets,
            };
        }

        /// <summary>The operation names the write host reports.</summary>
        private static class AccessOperation
        {
            /// <summary>A save.</summary>
            public const string Save = "save";

            /// <summary>A delete.</summary>
            public const string Delete = "delete";
        }

        /// <summary>The frame of a rule under test: the pipeline's, with no confirmations to raise.</summary>
        /// <param name="operation">The operation's name.</param>
        private sealed class RefusingWriteHost(string operation) : IWriteHost
        {
            /// <inheritdoc />
            public WriteHostKind Kind => WriteHostKind.Pipeline;

            /// <inheritdoc />
            public string Operation => operation;

            /// <inheritdoc />
            public IConfirmations Confirmations => throw new NotSupportedException(
                "A rule under test raised a confirmation, which the validation context of a unit test does not support.");
        }
    }
}
