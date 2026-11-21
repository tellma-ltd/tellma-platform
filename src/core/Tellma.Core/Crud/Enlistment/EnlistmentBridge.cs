// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Numerics;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Crud.Delete;
using Tellma.Core.Crud.Save;

namespace Tellma.Core.Crud.Enlistment
{
    // An enlistment names its target by the entity type the writer declared, which may be a pack
    // entity a distribution's leaf derives from; the target's stack runs over its leaf and key. The
    // bridge closes the stack's save and delete over them once per stack, so an enlistment costs no
    // reflection beyond its first.
    /// <summary>Creates the save and delete groups of a target stack, over the stack's leaf and key.</summary>
    internal static class EnlistmentBridge
    {
        /// <summary>One bridge per leaf and key, created on first use.</summary>
        private static readonly ConcurrentDictionary<(Type Leaf, Type Key), Bridge> Bridges = new();

        /// <summary>The bridge of a stack.</summary>
        /// <param name="target">The stack.</param>
        /// <returns>The bridge.</returns>
        public static Bridge Of(StackDescriptor target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return Bridges.GetOrAdd(
                (target.EntityType, target.KeyType),
                static key => (Bridge)Activator.CreateInstance(typeof(Bridge<,>).MakeGenericType(key.Leaf, key.Key))!);
        }

        /// <summary>Creates the groups of a stack.</summary>
        internal abstract class Bridge
        {
            /// <summary>Creates an enlisted save of the stack's rows.</summary>
            /// <param name="site">What the group needs of its host.</param>
            /// <param name="rows">The rows, of the stack's leaf.</param>
            /// <param name="options">How the save runs.</param>
            /// <returns>The group, its stages not started.</returns>
            /// <exception cref="InvalidOperationException">A row is not of the stack's leaf.</exception>
            public abstract IEnlistedSave Save(EnlistmentSite site, IReadOnlyList<object?> rows, EnlistSaveOptions options);

            /// <summary>Creates an enlisted delete of the stack's rows.</summary>
            /// <typeparam name="TKey">The key type the writer named the ids in.</typeparam>
            /// <param name="site">What the group needs of its host.</param>
            /// <param name="ids">The ids.</param>
            /// <returns>The group, its stages not started.</returns>
            /// <exception cref="InvalidOperationException">The ids are not of the stack's key type.</exception>
            public abstract IEnlistedDelete Delete<TKey>(EnlistmentSite site, IReadOnlyList<TKey> ids)
                where TKey : struct;
        }

        /// <summary>The groups of a stack over its leaf and key.</summary>
        /// <typeparam name="TEntity">The stack's leaf.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        private sealed class Bridge<TEntity, TKey> : Bridge
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            /// <inheritdoc />
            public override IEnlistedSave Save(EnlistmentSite site, IReadOnlyList<object?> rows, EnlistSaveOptions options)
            {
                // --- The writer's own instances, as the leaf's: the ids land on the rows it holds ---
                IReadOnlyList<TEntity> typed = rows as IReadOnlyList<TEntity> ?? [.. rows.Select(row => row is null or TEntity
                    ? (TEntity)row!
                    : throw new InvalidOperationException(
                        $"A row enlisted with {site.Host.Operation} is a {row.GetType().Name}, not a {typeof(TEntity).Name}, the leaf the stack of {site.Target.Resource} saves."))];
                SaveOperation services = site.Host.Rounds.Services;
                return new SaveRun<TEntity, TKey>(services, site.Target, Behavior(services, site.Target), typed, Options(site, options), site.Host.Rounds.CancellationToken, new EnlistedSaveSite(site, options));
            }

            /// <inheritdoc />
            public override IEnlistedDelete Delete<TIds>(EnlistmentSite site, IReadOnlyList<TIds> ids)
            {
                IReadOnlyList<TKey> typed = ids as IReadOnlyList<TKey> ?? throw new InvalidOperationException(
                    $"The ids enlisted with {site.Host.Operation} are {typeof(TIds).Name} values; the stack of {site.Target.Resource} keys its rows by {typeof(TKey).Name}.");
                SaveOperation services = site.Host.Rounds.Services;
                return new DeleteRun<TEntity, TKey>(services, site.Target, Behavior(services, site.Target), CrudOperationNames.Delete, site.Host.Rounds.CancellationToken, site).Enlisted(typed);
            }

            /// <summary>The target stack's hooks, its service resolved from the scope.</summary>
            /// <param name="services">The scope's write services.</param>
            /// <param name="target">The stack.</param>
            /// <returns>The hooks.</returns>
            private static IEntityBehavior<TEntity, TKey> Behavior(SaveOperation services, StackDescriptor target)
            {
                return (IEntityBehavior<TEntity, TKey>)services.Services.GetRequiredService(target.ServiceType);
            }

            /// <summary>The save options an enlisted save runs under: the enlistment's concurrency and source, and no read-back.</summary>
            /// <param name="site">What the group needs of its host.</param>
            /// <param name="options">How the enlistment asked the save to run.</param>
            /// <returns>The options.</returns>
            private static SaveOptions Options(EnlistmentSite site, EnlistSaveOptions options)
            {
                return new SaveOptions
                {
                    ReturnEntities = false,
                    Concurrency = options.Concurrency,
                    Source = site.Source,
                };
            }
        }
    }

    /// <summary>What an enlisted save needs beyond its host: the server-owned members the writer supplies.</summary>
    /// <param name="Site">What the group needs of its host.</param>
    /// <param name="Options">How the enlistment asked the save to run.</param>
    internal sealed record EnlistedSaveSite(EnlistmentSite Site, EnlistSaveOptions Options);
}
