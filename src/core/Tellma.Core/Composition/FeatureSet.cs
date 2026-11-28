// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     The features of one composition in the order they were added, with the rule for adding one:
    ///     a type is added once, the same instance again is a no-op, and a second instance of an added
    ///     type is a problem.
    /// </summary>
    internal sealed class FeatureSet
    {
        private readonly List<ComposedFeature> _features = [];
        private readonly List<CompositionProblem> _problems = [];

        /// <summary>The features in the order they were added.</summary>
        public IReadOnlyList<ComposedFeature> Features => _features;

        /// <summary>The problems adding features produced.</summary>
        public IReadOnlyList<CompositionProblem> Problems => _problems;

        /// <summary>Adds a feature of <typeparamref name="TFeature" /> unless one is already added.</summary>
        /// <typeparam name="TFeature">The feature type.</typeparam>
        /// <returns>The added or already present feature.</returns>
        public ComposedFeature Add<TFeature>()
            where TFeature : ITellmaFeature, new()
        {
            return Find(typeof(TFeature)) ?? Append(new TFeature());
        }

        /// <summary>
        ///     Adds <paramref name="feature" />. The same instance again is a no-op; another instance
        ///     of an added type is a problem, and the instance added first stays.
        /// </summary>
        /// <param name="feature">The feature.</param>
        /// <returns>The added or already present feature.</returns>
        public ComposedFeature Add(ITellmaFeature feature)
        {
            ArgumentNullException.ThrowIfNull(feature);

            ComposedFeature? existing = Find(feature.GetType());
            if (existing is null)
            {
                return Append(feature);
            }

            if (!ReferenceEquals(existing.Instance, feature))
            {
                string type = TypeDisplay.Short(feature.GetType());
                _problems.Add(new CompositionProblem(
                    CompositionNames.Source,
                    $"The feature type {TypeDisplay.Qualified(feature.GetType())} was added twice, as two different instances.",
                    $"Add {type} once; AddFeature<{type}>() is idempotent, and only one instance of a feature type can be composed."));
            }

            return existing;
        }

        /// <summary>The added feature of exactly <paramref name="type" />.</summary>
        /// <param name="type">The feature type.</param>
        /// <returns>The feature, or null when none of that type is added.</returns>
        private ComposedFeature? Find(Type type)
        {
            return _features.Find(composed => composed.Instance.GetType() == type);
        }

        /// <summary>Adds a feature at the next index.</summary>
        /// <param name="feature">The feature.</param>
        /// <returns>The added feature.</returns>
        private ComposedFeature Append(ITellmaFeature feature)
        {
            ComposedFeature composed = new(feature, _features.Count);
            _features.Add(composed);
            return composed;
        }
    }
}
