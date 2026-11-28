// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Tests.Infrastructure
{
    /// <summary>
    ///     A feature whose name, declaration and contribution a test supplies. Each tag type closes a
    ///     distinct feature type, since a composition holds one instance per feature type.
    /// </summary>
    /// <typeparam name="TTag">Distinguishes one test feature type from another.</typeparam>
    /// <param name="name">The feature's name.</param>
    /// <param name="declare">What the feature declares.</param>
    /// <param name="contribute">What the feature contributes.</param>
    internal sealed class TestFeature<TTag>(
        string name,
        Action<FeatureDeclaration>? declare = null,
        Action<FeatureContribution>? contribute = null) : ITellmaFeature
    {
        /// <inheritdoc />
        public string Name { get; } = name;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
            declare?.Invoke(declaration);
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            contribute?.Invoke(contribution);
        }
    }

    /// <summary>Tags that close distinct <see cref="TestFeature{TTag}" /> types.</summary>
    internal static class Tag
    {
        /// <summary>The first tag.</summary>
        internal sealed class A;

        /// <summary>The second tag.</summary>
        internal sealed class B;

        /// <summary>The third tag.</summary>
        internal sealed class C;

        /// <summary>The fourth tag.</summary>
        internal sealed class D;
    }

    /// <summary>A feature with a fixed name and no declaration, addable with <c>AddFeature&lt;T&gt;()</c>.</summary>
    internal sealed class PlainFeature : ITellmaFeature
    {
        /// <inheritdoc />
        public string Name => "plain";

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
        }
    }

    /// <summary>A feature that requires <c>gl</c> by attribute and by declaration.</summary>
    [Requires("gl")]
    internal sealed class AttributedFeature : ITellmaFeature
    {
        /// <inheritdoc />
        public string Name => "acme";

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
            declaration.Requires("gl");
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
        }
    }
}
