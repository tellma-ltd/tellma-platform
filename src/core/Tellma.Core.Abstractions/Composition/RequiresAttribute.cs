// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     Declares, on a feature class, that the feature requires another feature by its
    ///     <see cref="ITellmaFeature.Name" />. Equivalent to calling
    ///     <see cref="FeatureDeclaration.Requires(string)" /> from <see cref="ITellmaFeature.Declare" />;
    ///     both may be used, and a name declared both ways counts once.
    /// </summary>
    /// <remarks>
    ///     Take the name from the constant the required pack's Abstractions package exposes rather
    ///     than spelling it out. The <c>core</c> feature is implicit and never needs declaring.
    /// </remarks>
    /// <param name="featureName">The required feature's name.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class RequiresAttribute(string featureName) : Attribute
    {
        /// <summary>The required feature's name.</summary>
        public string FeatureName { get; } = featureName;
    }
}
