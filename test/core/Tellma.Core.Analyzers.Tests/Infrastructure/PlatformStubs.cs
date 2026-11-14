// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Analyzers.Tests.Infrastructure
{
    /// <summary>
    ///     The platform types the rules match that a test compilation does not reference: the
    ///     runtime's tenant context, and the service pipeline's contract types. The rules match them
    ///     by namespace and metadata name, which these declarations share with the real ones; the
    ///     name pins hold the real types to the same names.
    /// </summary>
    internal static class PlatformStubs
    {
        /// <summary>The stubs' source, compiled into every test compilation beside the test's own source.</summary>
        public const string Source = """
            using System.Collections.Generic;
            using System.Numerics;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Tellma.Core.Abstractions.Entities;
            using Tellma.Core.Abstractions.Jobs;

            namespace Tellma.Core.Data
            {
                public sealed class TellmaDbContext : DbContext
                {
                    public DbSet<Job> Jobs { get; set; } = null!;
                }
            }

            namespace Tellma.Core.Abstractions.Crud
            {
                public abstract class EntityService<TEntity, TKey>
                    where TEntity : Entity<TKey>
                    where TKey : struct, IBinaryInteger<TKey>
                {
                    public Task SaveAsync(IReadOnlyList<TEntity> entities) => Task.CompletedTask;

                    public Task DeleteByIdsAsync(IReadOnlyList<TKey> ids) => Task.CompletedTask;

                    public Task DeleteByQueryAsync(string filter) => Task.CompletedTask;

                    public Task ExecuteActionAsync(string action, IReadOnlyList<TKey> ids) => Task.CompletedTask;

                    public Task<T> ExecuteActionAsync<T>(string action, IReadOnlyList<TKey> ids) => Task.FromResult(default(T)!);

                    public Task<IReadOnlyList<TEntity>> GetByIdsAsync(IReadOnlyList<TKey> ids) => Task.FromResult<IReadOnlyList<TEntity>>([]);

                    protected virtual Task ValidateAsync(IReadOnlyList<TEntity> entities) => Task.CompletedTask;

                    protected virtual Task AfterCommitAsync(IReadOnlyList<TEntity> entities) => Task.CompletedTask;
                }

                public abstract class EntityService<TEntity> : EntityService<TEntity, int>
                    where TEntity : Entity<int>;

                public static class EntityServiceExtensions
                {
                    public static Task ActivateAsync<TEntity, TKey>(this EntityService<TEntity, TKey> service, IReadOnlyList<TKey> ids)
                        where TEntity : Entity<TKey>
                        where TKey : struct, IBinaryInteger<TKey> => Task.CompletedTask;

                    public static Task DeactivateAsync<TEntity, TKey>(this EntityService<TEntity, TKey> service, IReadOnlyList<TKey> ids)
                        where TEntity : Entity<TKey>
                        where TKey : struct, IBinaryInteger<TKey> => Task.CompletedTask;

                    public static Task DeleteWithDescendantsAsync<TEntity, TKey>(this EntityService<TEntity, TKey> service, IReadOnlyList<TKey> ids)
                        where TEntity : Entity<TKey>
                        where TKey : struct, IBinaryInteger<TKey> => Task.CompletedTask;
                }

                public interface IEntityValidator<TEntity>
                {
                    Task ValidateAsync(IReadOnlyList<TEntity> entities);
                }

                public interface IPersistEffect<TEntity>
                {
                    Task ContributeAsync(IReadOnlyList<TEntity> entities);
                }
            }
            """;
    }
}
