// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Tellma.Identity.Tests.Provisioning
{
    /// <summary>
    ///     An OpenIddict application row held in memory. Every member is what the EF store would
    ///     persist in a column, so the real <c>OpenIddictApplicationManager</c> over
    ///     <see cref="InMemoryApplicationStore" /> behaves as it does over SQL Server: secrets hashed,
    ///     descriptors validated, permissions read back as stored.
    /// </summary>
    public sealed class InMemoryApplication
    {
        /// <summary>The row key.</summary>
        public string Id { get; } = Guid.NewGuid().ToString("N");

        /// <summary>The stored ApplicationType column.</summary>
        public string? ApplicationType { get; set; }

        /// <summary>The stored ClientId column.</summary>
        public string? ClientId { get; set; }

        /// <summary>The stored ClientSecret column.</summary>
        public string? ClientSecret { get; set; }

        /// <summary>The stored ClientType column.</summary>
        public string? ClientType { get; set; }

        /// <summary>The stored ConsentType column.</summary>
        public string? ConsentType { get; set; }

        /// <summary>The stored DisplayName column.</summary>
        public string? DisplayName { get; set; }

        /// <summary>The stored DisplayNames column.</summary>
        public ImmutableDictionary<CultureInfo, string> DisplayNames { get; set; } = [];

        /// <summary>The stored JsonWebKeySet column.</summary>
        public JsonWebKeySet? JsonWebKeySet { get; set; }

        /// <summary>The stored Permissions column.</summary>
        public ImmutableArray<string> Permissions { get; set; } = [];

        /// <summary>The stored PostLogoutRedirectUris column.</summary>
        public ImmutableArray<string> PostLogoutRedirectUris { get; set; } = [];

        /// <summary>The stored Properties column.</summary>
        public ImmutableDictionary<string, JsonElement> Properties { get; set; } = [];

        /// <summary>The stored RedirectUris column.</summary>
        public ImmutableArray<string> RedirectUris { get; set; } = [];

        /// <summary>The stored Requirements column.</summary>
        public ImmutableArray<string> Requirements { get; set; } = [];

        /// <summary>The stored Settings column.</summary>
        public ImmutableDictionary<string, string> Settings { get; set; } = [];
    }

    /// <summary>An OpenIddict scope row held in memory.</summary>
    public sealed class InMemoryScope
    {
        /// <summary>The row key.</summary>
        public string Id { get; } = Guid.NewGuid().ToString("N");

        /// <summary>The stored Name column.</summary>
        public string? Name { get; set; }

        /// <summary>The stored Description column.</summary>
        public string? Description { get; set; }

        /// <summary>The stored Descriptions column.</summary>
        public ImmutableDictionary<CultureInfo, string> Descriptions { get; set; } = [];

        /// <summary>The stored DisplayName column.</summary>
        public string? DisplayName { get; set; }

        /// <summary>The stored DisplayNames column.</summary>
        public ImmutableDictionary<CultureInfo, string> DisplayNames { get; set; } = [];

        /// <summary>The stored Properties column.</summary>
        public ImmutableDictionary<string, JsonElement> Properties { get; set; } = [];

        /// <summary>The stored Resources column.</summary>
        public ImmutableArray<string> Resources { get; set; } = [];
    }

    /// <summary>
    ///     The application table: every row, and a count of each write, so a test can tell a
    ///     convergence that wrote nothing from one that rewrote a row with the same values.
    /// </summary>
    public sealed class InMemoryApplicationStore : IOpenIddictApplicationStore<InMemoryApplication>
    {
        /// <summary>The rows.</summary>
        private readonly List<InMemoryApplication> _rows = [];

        /// <summary>Every row, in insertion order.</summary>
        public IReadOnlyList<InMemoryApplication> Rows => _rows;

        /// <summary>How many rows were created.</summary>
        public int Creates { get; private set; }

        /// <summary>How many times a row was written back.</summary>
        public int Updates { get; private set; }

        /// <summary>How many rows were deleted.</summary>
        public int Deletes { get; private set; }

        /// <summary>The row with the client id, or null.</summary>
        /// <param name="clientId">The value to match.</param>
        /// <returns>The row, or null.</returns>
        public InMemoryApplication? Find(string clientId)
        {
            return _rows.SingleOrDefault(row => string.Equals(row.ClientId, clientId, StringComparison.Ordinal));
        }

        /// <inheritdoc />
        public ValueTask<long> CountAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult((long)_rows.Count);
        }

        /// <inheritdoc />
        public ValueTask<long> CountAsync<TResult>(
            Func<IQueryable<InMemoryApplication>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult((long)query(_rows.AsQueryable()).Count());
        }

        /// <inheritdoc />
        public ValueTask CreateAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            _rows.Add(application);
            Creates++;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DeleteAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            _rows.Remove(application);
            Deletes++;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<InMemoryApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_rows.SingleOrDefault(row => row.Id == identifier));
        }

        /// <inheritdoc />
        public ValueTask<InMemoryApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Find(identifier));
        }

        /// <inheritdoc />
        public IAsyncEnumerable<InMemoryApplication> FindByPostLogoutRedirectUriAsync(string uri, CancellationToken cancellationToken)
        {
            return Enumerate(_rows.Where(row => row.PostLogoutRedirectUris.Contains(uri)).ToList(), cancellationToken);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<InMemoryApplication> FindByRedirectUriAsync(string uri, CancellationToken cancellationToken)
        {
            return Enumerate(_rows.Where(row => row.RedirectUris.Contains(uri)).ToList(), cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetApplicationTypeAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.ApplicationType);
        }

        /// <inheritdoc />
        public ValueTask<TResult?> GetAsync<TState, TResult>(
            Func<IQueryable<InMemoryApplication>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(query(_rows.AsQueryable(), state).FirstOrDefault());
        }

        /// <inheritdoc />
        public ValueTask<string?> GetClientIdAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.ClientId);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetClientSecretAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.ClientSecret);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetClientTypeAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.ClientType);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetConsentTypeAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.ConsentType);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetDisplayNameAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.DisplayName);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
            InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.DisplayNames);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetIdAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<string?>(application.Id);
        }

        /// <inheritdoc />
        public ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.JsonWebKeySet);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableArray<string>> GetPermissionsAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.Permissions);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(
            InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.PostLogoutRedirectUris);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
            InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.Properties);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.RedirectUris);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableArray<string>> GetRequirementsAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.Requirements);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(
            InMemoryApplication application, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(application.Settings);
        }

        /// <inheritdoc />
        public ValueTask<InMemoryApplication> InstantiateAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new InMemoryApplication());
        }

        /// <inheritdoc />
        public IAsyncEnumerable<InMemoryApplication> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
        {
            IEnumerable<InMemoryApplication> rows = _rows.Skip(offset ?? 0);
            return Enumerate((count is null ? rows : rows.Take(count.Value)).ToList(), cancellationToken);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
            Func<IQueryable<InMemoryApplication>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)
        {
            return Enumerate(query(_rows.AsQueryable(), state).ToList(), cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask SetApplicationTypeAsync(InMemoryApplication application, string? type, CancellationToken cancellationToken)
        {
            application.ApplicationType = type;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetClientIdAsync(InMemoryApplication application, string? identifier, CancellationToken cancellationToken)
        {
            application.ClientId = identifier;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetClientSecretAsync(InMemoryApplication application, string? secret, CancellationToken cancellationToken)
        {
            application.ClientSecret = secret;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetClientTypeAsync(InMemoryApplication application, string? type, CancellationToken cancellationToken)
        {
            application.ClientType = type;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetConsentTypeAsync(InMemoryApplication application, string? type, CancellationToken cancellationToken)
        {
            application.ConsentType = type;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetDisplayNameAsync(InMemoryApplication application, string? name, CancellationToken cancellationToken)
        {
            application.DisplayName = name;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetDisplayNamesAsync(
            InMemoryApplication application, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
        {
            application.DisplayNames = names;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetJsonWebKeySetAsync(InMemoryApplication application, JsonWebKeySet? set, CancellationToken cancellationToken)
        {
            application.JsonWebKeySet = set;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetPermissionsAsync(
            InMemoryApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken)
        {
            application.Permissions = permissions;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetPostLogoutRedirectUrisAsync(
            InMemoryApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
        {
            application.PostLogoutRedirectUris = uris;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetPropertiesAsync(
            InMemoryApplication application, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
        {
            application.Properties = properties;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetRedirectUrisAsync(InMemoryApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
        {
            application.RedirectUris = uris;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetRequirementsAsync(
            InMemoryApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken)
        {
            application.Requirements = requirements;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetSettingsAsync(
            InMemoryApplication application, ImmutableDictionary<string, string> settings, CancellationToken cancellationToken)
        {
            application.Settings = settings;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask UpdateAsync(InMemoryApplication application, CancellationToken cancellationToken)
        {
            Updates++;
            return ValueTask.CompletedTask;
        }

        /// <summary>Streams a materialized list, as the EF store streams a query.</summary>
        /// <typeparam name="T">The item type.</typeparam>
        /// <param name="items">The items.</param>
        /// <param name="cancellationToken">Stops the stream.</param>
        /// <returns>The stream.</returns>
        private static async IAsyncEnumerable<T> Enumerate<T>(
            IReadOnlyList<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (T item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            await Task.CompletedTask;
        }
    }

    /// <summary>The scope table: every row, and a count of each update.</summary>
    public sealed class InMemoryScopeStore : IOpenIddictScopeStore<InMemoryScope>
    {
        /// <summary>The rows.</summary>
        private readonly List<InMemoryScope> _rows = [];

        /// <summary>Every row, in insertion order.</summary>
        public IReadOnlyList<InMemoryScope> Rows => _rows;

        /// <summary>How many times a row was written back.</summary>
        public int Updates { get; private set; }

        /// <summary>The row with the scope name, or null.</summary>
        /// <param name="name">The value to match.</param>
        /// <returns>The row, or null.</returns>
        public InMemoryScope? Find(string name)
        {
            return _rows.SingleOrDefault(row => string.Equals(row.Name, name, StringComparison.Ordinal));
        }

        /// <inheritdoc />
        public ValueTask<long> CountAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult((long)_rows.Count);
        }

        /// <inheritdoc />
        public ValueTask<long> CountAsync<TResult>(
            Func<IQueryable<InMemoryScope>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult((long)query(_rows.AsQueryable()).Count());
        }

        /// <inheritdoc />
        public ValueTask CreateAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            _rows.Add(scope);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DeleteAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            _rows.Remove(scope);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<InMemoryScope?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_rows.SingleOrDefault(row => row.Id == identifier));
        }

        /// <inheritdoc />
        public ValueTask<InMemoryScope?> FindByNameAsync(string name, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Find(name));
        }

        /// <inheritdoc />
        public IAsyncEnumerable<InMemoryScope> FindByNamesAsync(ImmutableArray<string> names, CancellationToken cancellationToken)
        {
            return Enumerate(_rows.Where(row => row.Name is not null && names.Contains(row.Name)).ToList(), cancellationToken);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<InMemoryScope> FindByResourceAsync(string resource, CancellationToken cancellationToken)
        {
            return Enumerate(_rows.Where(row => row.Resources.Contains(resource)).ToList(), cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask<TResult?> GetAsync<TState, TResult>(
            Func<IQueryable<InMemoryScope>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(query(_rows.AsQueryable(), state).FirstOrDefault());
        }

        /// <inheritdoc />
        public ValueTask<string?> GetDescriptionAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(scope.Description);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(scope.Descriptions);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetDisplayNameAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(scope.DisplayName);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(scope.DisplayNames);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetIdAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<string?>(scope.Id);
        }

        /// <inheritdoc />
        public ValueTask<string?> GetNameAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(scope.Name);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(scope.Properties);
        }

        /// <inheritdoc />
        public ValueTask<ImmutableArray<string>> GetResourcesAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(scope.Resources);
        }

        /// <inheritdoc />
        public ValueTask<InMemoryScope> InstantiateAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new InMemoryScope());
        }

        /// <inheritdoc />
        public IAsyncEnumerable<InMemoryScope> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
        {
            IEnumerable<InMemoryScope> rows = _rows.Skip(offset ?? 0);
            return Enumerate((count is null ? rows : rows.Take(count.Value)).ToList(), cancellationToken);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
            Func<IQueryable<InMemoryScope>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken)
        {
            return Enumerate(query(_rows.AsQueryable(), state).ToList(), cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask SetDescriptionAsync(InMemoryScope scope, string? description, CancellationToken cancellationToken)
        {
            scope.Description = description;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetDescriptionsAsync(
            InMemoryScope scope, ImmutableDictionary<CultureInfo, string> descriptions, CancellationToken cancellationToken)
        {
            scope.Descriptions = descriptions;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetDisplayNameAsync(InMemoryScope scope, string? name, CancellationToken cancellationToken)
        {
            scope.DisplayName = name;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetDisplayNamesAsync(
            InMemoryScope scope, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
        {
            scope.DisplayNames = names;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetNameAsync(InMemoryScope scope, string? name, CancellationToken cancellationToken)
        {
            scope.Name = name;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetPropertiesAsync(
            InMemoryScope scope, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
        {
            scope.Properties = properties;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask SetResourcesAsync(InMemoryScope scope, ImmutableArray<string> resources, CancellationToken cancellationToken)
        {
            scope.Resources = resources;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask UpdateAsync(InMemoryScope scope, CancellationToken cancellationToken)
        {
            Updates++;
            return ValueTask.CompletedTask;
        }

        /// <summary>Streams a materialized list, as the EF store streams a query.</summary>
        /// <typeparam name="T">The item type.</typeparam>
        /// <param name="items">The items.</param>
        /// <param name="cancellationToken">Stops the stream.</param>
        /// <returns>The stream.</returns>
        private static async IAsyncEnumerable<T> Enumerate<T>(
            IReadOnlyList<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (T item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            await Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Registers OpenIddict's real application and scope managers over the in-memory stores,
    ///     with entity caching off so every read goes to the rows a test inspects.
    /// </summary>
    public static class InMemoryOpenIddict
    {
        /// <summary>Adds the managers and the two stores, as singletons the test can read back.</summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The same collection.</returns>
        public static IServiceCollection AddInMemoryOpenIddict(this IServiceCollection services)
        {
            services.AddLogging();
            services.AddSingleton<InMemoryApplicationStore>();
            services.AddSingleton<InMemoryScopeStore>();
            services.AddOpenIddict().AddCore(core =>
            {
                core.SetDefaultApplicationEntity<InMemoryApplication>();
                core.SetDefaultScopeEntity<InMemoryScope>();
                core.ReplaceApplicationStore<InMemoryApplication, InMemoryApplicationStore>(ServiceLifetime.Singleton);
                core.ReplaceScopeStore<InMemoryScope, InMemoryScopeStore>(ServiceLifetime.Singleton);
                core.DisableEntityCaching();
            });

            // The managers resolve the store through its interface; point that at the singleton the
            // test reads, rather than a second instance.
            services.AddSingleton<IOpenIddictApplicationStore<InMemoryApplication>>(
                static provider => provider.GetRequiredService<InMemoryApplicationStore>());
            services.AddSingleton<IOpenIddictScopeStore<InMemoryScope>>(
                static provider => provider.GetRequiredService<InMemoryScopeStore>());
            return services;
        }
    }
}
