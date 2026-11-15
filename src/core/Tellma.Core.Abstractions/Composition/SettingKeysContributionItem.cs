// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A type whose public static setting-key fields a feature contributes: every field of type
    ///     <see cref="Settings.SettingKey{T}" /> joins the composition's setting-key registry, in
    ///     declaration order.
    /// </summary>
    /// <param name="DeclaringType">The type that declares the fields.</param>
    public sealed record SettingKeysContributionItem(Type DeclaringType) : FeatureContributionItem;
}
