// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>One feature of a composition and what the pipeline learned about it.</summary>
    /// <param name="instance">The feature.</param>
    /// <param name="index">The order in which the feature was added; ties in the realisation order break by it.</param>
    internal sealed class ComposedFeature(ITellmaFeature instance, int index)
    {
        /// <summary>The feature.</summary>
        public ITellmaFeature Instance { get; } = instance;

        /// <summary>The order in which the feature was added, <c>core</c> being 0.</summary>
        public int Index { get; } = index;

        /// <summary>The feature's name, read once when it is declared.</summary>
        public string? Name { get; set; }

        /// <summary>The required feature names, from <see cref="FeatureDeclaration.Requires" /> and <see cref="RequiresAttribute" />, without repeats.</summary>
        public List<string> RequiredNames { get; } = [];

        /// <summary>The options bindings the feature declared.</summary>
        public List<FeatureOptionsBinding> OptionsBindings { get; } = [];

        /// <summary>The feature's contribution, once collected.</summary>
        public FeatureContribution? Contribution { get; set; }

        /// <summary>The features this one nests; each is realised before it.</summary>
        public List<ComposedFeature> NestedFeatures { get; } = [];

        /// <summary>A label for problem texts: the name when it is usable, else the type.</summary>
        public string Label => string.IsNullOrWhiteSpace(Name)
            ? TypeDisplay.Qualified(Instance.GetType())
            : $"'{Name}' ({TypeDisplay.Qualified(Instance.GetType())})";
    }
}
