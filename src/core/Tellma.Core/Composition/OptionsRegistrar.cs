// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     Binds the composition's options types from configuration with data-annotation validation,
    ///     and records each so the <c>core.options</c> startup check validates them all at start.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Validation at start is the startup gate's, not the host's <c>ValidateOnStart</c>: the
    ///         host runs <c>ValidateOnStart</c> validators before any hosted service starts and stops
    ///         at the first failing set, so an invalid option would hide every other startup problem.
    ///         Through the gate, option failures are reported together with every other check's.
    ///     </para>
    ///     <para>
    ///         Features bind through their declarations; the platform's own options bind through
    ///         <see cref="Bind{TOptions}(string, string)" /> with the owner <c>core</c>.
    ///     </para>
    /// </remarks>
    /// <param name="context">The realisation: the services bindings register into, the record they are
    ///     recorded in and where a conflicting binding is reported.</param>
    /// <param name="configuration">The configuration options bind from.</param>
    internal sealed class OptionsRegistrar(RealizationContext context, IConfiguration configuration)
        : IFeatureOptionsBindingVisitor
    {
        private string _owner = CoreFeature.FeatureName;

        /// <summary>Binds a feature's declared options, once per type and path.</summary>
        /// <param name="binding">The declared binding.</param>
        /// <param name="owner">The name of the declaring feature.</param>
        public void Bind(FeatureOptionsBinding binding, string owner)
        {
            _owner = owner;
            binding.Accept(this);
        }

        /// <summary>Binds <typeparamref name="TOptions" /> from <paramref name="configurationPath" />, once per type and path.</summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        /// <param name="configurationPath">The configuration path.</param>
        /// <param name="owner">The name of the feature the binding belongs to.</param>
        public void Bind<TOptions>(string configurationPath, string owner)
            where TOptions : class
        {
            // The same type and path again is one binding declared twice, which binds once. Another
            // path is a conflict: the graph gate refuses it between features, but a platform binding
            // made through this seam meets a feature's only here.
            BoundOptions? existing = context.Record.BoundOptions.Find(bound => bound.OptionsType == typeof(TOptions));
            if (existing is not null)
            {
                if (!string.Equals(existing.ConfigurationPath, configurationPath, StringComparison.OrdinalIgnoreCase))
                {
                    context.Report(new CompositionProblem(
                        CompositionNames.Source,
                        $"The options type {TypeDisplay.Qualified(typeof(TOptions))} is bound from '{existing.ConfigurationPath}' (by '{existing.Owner}') and '{configurationPath}' (by '{owner}').",
                        $"Bind {TypeDisplay.Short(typeof(TOptions))} from one configuration path; a second use needs an options type of its own."));
                }

                return;
            }

            _ = context.Services
                .AddOptions<TOptions>()
                .Bind(configuration.GetSection(configurationPath))
                .ValidateDataAnnotations();

            context.Record.BoundOptions.Add(new BoundOptions(typeof(TOptions), configurationPath, owner, ValidateOptions<TOptions>));
        }

        /// <inheritdoc />
        void IFeatureOptionsBindingVisitor.Visit<TOptions>(FeatureOptionsBinding binding)
        {
            Bind<TOptions>(binding.ConfigurationPath, _owner);
        }

        /// <summary>Resolves <typeparamref name="TOptions" />, which binds and validates it.</summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        /// <param name="services">The services to resolve from.</param>
        /// <returns>Each validation or binding failure; empty when the options are valid.</returns>
        internal static IReadOnlyList<string> ValidateOptions<TOptions>(IServiceProvider services)
            where TOptions : class
        {
            try
            {
                // Resolving the value runs binding and every registered validator.
                _ = services.GetRequiredService<IOptions<TOptions>>().Value;
                return [];
            }
            catch (OptionsValidationException exception)
            {
                return [.. exception.Failures];
            }
            catch (InvalidOperationException exception)
            {
                // The binder throws this for a value it cannot convert to the property's type.
                return [exception.Message];
            }
        }
    }
}
