// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tellma.Core.AspNetCore.OpenApi
{
    // The OpenAPI generator builds every schema under a copy of the HTTP JSON options it resolves
    // (IOptions<JsonOptions>), nesting limit included: the exporter refuses a schema deeper than the
    // limit, and so does the reader the schema is parsed back with, failing the whole document. The
    // HTTP options must keep the wire's limit of 16 (spec 0016 §3.1), so the generator's services
    // that resolve those options are registered again here, each handed options built by the same
    // configuration with the document's own limit in place of the wire's.

    /// <summary>The JSON options the document's schema generator reads: the host's, under the document's nesting limit.</summary>
    internal static class SchemaJson
    {
        /// <summary>
        ///     Registers each of a document's services that reads the HTTP JSON options again, handed
        ///     the document's options instead; every other service keeps the HTTP options.
        /// </summary>
        /// <param name="services">The host's services, the document already added.</param>
        /// <param name="documentName">The document's name, which keys its services.</param>
        /// <exception cref="InvalidOperationException">No service of the document reads the HTTP JSON options.</exception>
        public static void Register(IServiceCollection services, string documentName)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrEmpty(documentName);
            int replaced = 0;
            for (int i = 0; i < services.Count; i++)
            {
                // --- A service keyed by the document whose constructor takes the HTTP JSON options ---
                ServiceDescriptor descriptor = services[i];
                if (!descriptor.IsKeyedService
                    || !Equals(descriptor.ServiceKey, documentName)
                    || descriptor.KeyedImplementationType is not Type implementation
                    || !ReadsJsonOptions(implementation))
                {
                    continue;
                }

                // --- The same service, its key passed as its key parameter, built over the document's options ---
                services[i] = new ServiceDescriptor(
                    descriptor.ServiceType,
                    descriptor.ServiceKey,
                    (provider, key) => ActivatorUtilities.CreateInstance(provider, implementation, key!, Options.Create(Create(provider))),
                    descriptor.Lifetime);
                replaced++;
            }

            // A generator that no longer reads the options this way would quietly fall back to the
            // wire's limit, so its absence fails composition.
            if (replaced == 0)
            {
                throw new InvalidOperationException(
                    $"The OpenAPI document '{documentName}' registers no service that reads the HTTP JSON options, so its schemas cannot take the document's nesting limit.");
            }
        }

        /// <summary>
        ///     Builds the document's JSON options: a fresh instance under every configuration the
        ///     HTTP JSON options receive, with the document's nesting limit.
        /// </summary>
        /// <param name="services">The application's services.</param>
        /// <returns>The options; the HTTP JSON options are left as they are.</returns>
        public static JsonOptions Create(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            JsonOptions options = services.GetRequiredService<IOptionsFactory<JsonOptions>>().Create(Options.DefaultName);
            options.SerializerOptions.MaxDepth = WebOpenApi.SchemaMaxDepth;
            return options;
        }

        /// <summary>Whether a type's constructor takes the HTTP JSON options.</summary>
        /// <param name="implementation">The type.</param>
        /// <returns>True when one of its public constructors takes them.</returns>
        private static bool ReadsJsonOptions(Type implementation)
        {
            return implementation.GetConstructors()
                .Any(static constructor => constructor.GetParameters().Any(static parameter => parameter.ParameterType == typeof(IOptions<JsonOptions>)));
        }
    }
}
