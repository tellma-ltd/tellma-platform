// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     What a feature records in <see cref="ITellmaFeature.Declare" />: the features it requires
    ///     and the options types it binds from configuration. Recording only; the platform validates
    ///     the declarations of every feature together and reports every problem at once.
    /// </summary>
    public sealed class FeatureDeclaration
    {
        private readonly List<string> _requiredFeatures = [];
        private readonly List<FeatureOptionsBinding> _optionsBindings = [];

        /// <summary>
        ///     The names of the features this feature requires, in declaration order and without
        ///     repeats. <see cref="RequiresAttribute" /> edges are not included; the platform merges
        ///     them in.
        /// </summary>
        public IReadOnlyList<string> RequiredFeatures => _requiredFeatures;

        /// <summary>The options bindings recorded by <see cref="Options{TOptions}(string)" />, in order.</summary>
        public IReadOnlyList<FeatureOptionsBinding> OptionsBindings => _optionsBindings;

        /// <summary>
        ///     Requires another feature by its <see cref="ITellmaFeature.Name" />. A name no added
        ///     feature carries is a composition problem that names this feature. The <c>core</c>
        ///     feature is implicit and never needs declaring.
        /// </summary>
        /// <param name="featureName">The required feature's name.</param>
        /// <returns>This declaration, for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="featureName" /> is null, empty or whitespace.</exception>
        public FeatureDeclaration Requires(string featureName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

            // Declaring the same edge twice is harmless, so it collapses rather than failing.
            if (!_requiredFeatures.Contains(featureName, StringComparer.Ordinal))
            {
                _requiredFeatures.Add(featureName);
            }

            return this;
        }

        /// <summary>
        ///     Binds <typeparamref name="TOptions" /> from the configuration section at
        ///     <paramref name="configurationPath" />, validated by its data annotations before the host
        ///     serves traffic. The same type bound to two different paths, by this feature or any
        ///     other, is a composition problem.
        /// </summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        /// <param name="configurationPath">The full configuration path, such as <c>Tellma:Gl</c>.</param>
        /// <returns>This declaration, for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="configurationPath" /> is null, empty or whitespace.</exception>
        public FeatureDeclaration Options<TOptions>(string configurationPath)
            where TOptions : class
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
            _optionsBindings.Add(new FeatureOptionsBinding<TOptions>(configurationPath));
            return this;
        }
    }
}
