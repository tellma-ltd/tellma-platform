// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     One options binding a feature declared: an options type and the configuration path it
    ///     binds from. Created by <see cref="FeatureDeclaration.Options{TOptions}(string)" />.
    /// </summary>
    /// <remarks>
    ///     The options type is carried as a type argument as well as a <see cref="Type" />, and
    ///     handed back through <see cref="Accept" />, so the platform can bind it without reflection.
    /// </remarks>
    public abstract class FeatureOptionsBinding
    {
        /// <summary>Creates a binding; only the generic subclass in this assembly derives.</summary>
        /// <param name="configurationPath">The configuration path the options bind from.</param>
        private protected FeatureOptionsBinding(string configurationPath)
        {
            ConfigurationPath = configurationPath;
        }

        /// <summary>The options type.</summary>
        public abstract Type OptionsType { get; }

        /// <summary>The full configuration path the options bind from, such as <c>Tellma:Gl</c>.</summary>
        public string ConfigurationPath { get; }

        /// <summary>Calls <see cref="IFeatureOptionsBindingVisitor.Visit{TOptions}" /> with the options type.</summary>
        /// <param name="visitor">The visitor.</param>
        public abstract void Accept(IFeatureOptionsBindingVisitor visitor);
    }

    /// <summary>The binding of one options type, carrying the type as a type argument.</summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="configurationPath">The configuration path the options bind from.</param>
    internal sealed class FeatureOptionsBinding<TOptions>(string configurationPath)
        : FeatureOptionsBinding(configurationPath)
        where TOptions : class
    {
        /// <inheritdoc />
        public override Type OptionsType => typeof(TOptions);

        /// <inheritdoc />
        public override void Accept(IFeatureOptionsBindingVisitor visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);
            visitor.Visit<TOptions>(this);
        }
    }

    /// <summary>Receives a <see cref="FeatureOptionsBinding" /> with its options type as a type argument.</summary>
    public interface IFeatureOptionsBindingVisitor
    {
        /// <summary>Visits one binding.</summary>
        /// <typeparam name="TOptions">The binding's options type.</typeparam>
        /// <param name="binding">The binding.</param>
        void Visit<TOptions>(FeatureOptionsBinding binding)
            where TOptions : class;
    }
}
