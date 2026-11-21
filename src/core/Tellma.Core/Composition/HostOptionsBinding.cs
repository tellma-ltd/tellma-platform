// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     Binds a host adapter's options after the composition has been realised, under the same
    ///     rules a feature's options follow: bound once from configuration, validated with data
    ///     annotations and any registered validator, and checked by the realised gate's options check
    ///     rather than on start.
    /// </summary>
    internal static class HostOptionsBinding
    {
        /// <summary>Binds <typeparamref name="TOptions" /> from <paramref name="configurationPath" />.</summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        /// <param name="services">The composed services; <c>AddTellma</c> has run on them.</param>
        /// <param name="configurationPath">The configuration section path.</param>
        /// <param name="owner">What binds the options, as the options check names it.</param>
        /// <returns>The options builder, for further validation.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The services were not composed, or the type is already bound from another path.
        /// </exception>
        public static OptionsBuilder<TOptions> Bind<TOptions>(IServiceCollection services, string configurationPath, string owner)
            where TOptions : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(owner);

            // The composition's record is where the options check finds what to validate; a host
            // adapter that binds before AddTellma would bind options nothing ever checks.
            TellmaCompositionRecord record = services
                .Select(static descriptor => descriptor.ImplementationInstance)
                .OfType<TellmaCompositionRecord>()
                .SingleOrDefault()
                ?? throw new InvalidOperationException("A host adapter binds its options after AddTellma has composed the services.");

            BoundOptions? existing = record.BoundOptions.Find(bound => bound.OptionsType == typeof(TOptions));
            if (existing is not null)
            {
                // A second binding from another path would leave one of the two sections unread.
                return string.Equals(existing.ConfigurationPath, configurationPath, StringComparison.OrdinalIgnoreCase)
                    ? services.AddOptions<TOptions>()
                    : throw new InvalidOperationException(
                        $"The options type {TypeDisplay.Qualified(typeof(TOptions))} is bound from '{existing.ConfigurationPath}' (by '{existing.Owner}') and cannot also be bound from '{configurationPath}' (by '{owner}').");
            }

            OptionsBuilder<TOptions> builder = services
                .AddOptions<TOptions>()
                .Bind(record.Configuration.GetSection(configurationPath))
                .ValidateDataAnnotations();

            record.BoundOptions.Add(new BoundOptions(typeof(TOptions), configurationPath, owner, OptionsRegistrar.ValidateOptions<TOptions>));
            return builder;
        }
    }
}
