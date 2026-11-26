// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Dashboards
{
    /// <summary>One dashboard widget type as the composition registers it.</summary>
    /// <param name="Key">The type's key, such as <c>core.report</c> or <c>acme.cash-position</c>.</param>
    /// <param name="Feature">The name of the feature that registered the type.</param>
    /// <param name="SettingsType">The class a widget's settings bind to; null when the type has no settings.</param>
    /// <param name="Subject">What a widget of the type is about, beyond its settings.</param>
    public sealed record WidgetTypeDescriptor(string Key, string Feature, Type? SettingsType, WidgetSubject Subject);
}
