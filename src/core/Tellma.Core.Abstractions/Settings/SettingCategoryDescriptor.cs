// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>One settings tab: a category of setting keys and the securable that guards it.</summary>
    /// <param name="Name">The category (<c>general</c>, <c>gl</c>).</param>
    /// <param name="Resource">
    ///     The securable resource whose read and save grants admit a member to the tab:
    ///     <c>core.Settings.General</c> for <c>general</c> (shared with the tenant's typed settings),
    ///     else <c>core.Settings.</c> and the category verbatim.
    /// </param>
    /// <param name="Order">The tab's position: <c>general</c> first, then the categories in the order their first key was registered.</param>
    /// <param name="LabelKey">The label's resource key, <c>SettingCategory_</c> and the name.</param>
    public sealed record SettingCategoryDescriptor(string Name, string Resource, int Order, string LabelKey);
}
