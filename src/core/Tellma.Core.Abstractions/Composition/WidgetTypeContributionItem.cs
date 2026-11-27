// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A dashboard widget type a feature registers, once, under a key whose first segment is the
    ///     feature's name. The platform composes it into the widget type registry in realisation
    ///     order, and the startup gate validates it.
    /// </summary>
    /// <param name="Key">The type's key, such as <c>acme.cash-position</c>.</param>
    /// <param name="SettingsType">The class a widget's settings bind to; null when the type has no settings.</param>
    public sealed record WidgetTypeContributionItem(string Key, Type? SettingsType) : FeatureContributionItem;
}
