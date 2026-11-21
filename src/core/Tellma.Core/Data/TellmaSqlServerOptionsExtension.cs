// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     The options extension <see cref="TellmaSqlServerDbContextOptionsExtensions.UseTellmaSqlServer" />
    ///     adds: it installs the platform's model conventions and the model cache key that tells one
    ///     composition's model from another's. It carries no options of its own.
    /// </summary>
    internal sealed class TellmaSqlServerOptionsExtension : IDbContextOptionsExtension
    {
        /// <summary>Creates the extension.</summary>
        public TellmaSqlServerOptionsExtension()
        {
            Info = new ExtensionInfo(this);
        }

        /// <inheritdoc />
        public DbContextOptionsExtensionInfo Info { get; }

        /// <inheritdoc />
        public void ApplyServices(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // The plugin is a multi-registration service, so TryAdd adds it beside the table-type
            // extension's; the cache key factory replaces the provider's single one.
            _ = new EntityFrameworkServicesBuilder(services).TryAdd<IConventionSetPlugin, TellmaConventionSetPlugin>();
            _ = services.Replace(ServiceDescriptor.Singleton<IModelCacheKeyFactory, TellmaModelCacheKeyFactory>());
        }

        /// <inheritdoc />
        public void Validate(IDbContextOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
        }

        /// <summary>The options metadata of the extension: no options, and one shared internal service provider.</summary>
        /// <param name="extension">The extension.</param>
        private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            /// <inheritdoc />
            public override bool IsDatabaseProvider => false;

            /// <inheritdoc />
            public override string LogFragment => "using TellmaConventions ";

            /// <inheritdoc />
            public override int GetServiceProviderHashCode()
            {
                return 0;
            }

            /// <inheritdoc />
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            {
                return other is ExtensionInfo;
            }

            /// <inheritdoc />
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
                ArgumentNullException.ThrowIfNull(debugInfo);
                debugInfo["Tellma.Core:UseTellmaSqlServer"] = "1";
            }
        }
    }

    /// <summary>
    ///     Keys the model cache by the composition a <see cref="TellmaDbContext" /> was built from, so
    ///     two compositions in one process (a host and its tests, or two test compositions) never
    ///     share a model. Any other context keys by its type, as the provider's own factory does.
    /// </summary>
    internal sealed class TellmaModelCacheKeyFactory : IModelCacheKeyFactory
    {
        /// <inheritdoc />
        public object Create(DbContext context, bool designTime)
        {
            ArgumentNullException.ThrowIfNull(context);

            return context is TellmaDbContext tellma
                ? new ModelKey(context.GetType(), tellma.Definition, designTime)
                : new ModelKey(context.GetType(), null, designTime);
        }

        /// <summary>One model cache entry's key; the definition compares by reference.</summary>
        /// <param name="ContextType">The context type.</param>
        /// <param name="Definition">The composition's model definition, or <see langword="null" /> for another context.</param>
        /// <param name="DesignTime">Whether the design-time model is asked for.</param>
        private sealed record ModelKey(Type ContextType, TellmaModelDefinition? Definition, bool DesignTime);
    }
}
