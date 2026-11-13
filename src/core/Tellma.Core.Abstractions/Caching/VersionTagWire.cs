// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     The wire forms of the four tags a client tracks, and the value of the response header that
    ///     carries them: <c>settings=&lt;wire&gt;, permissions=&lt;wire&gt;, preferences=&lt;wire&gt;, entities=&lt;wire&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each wire form is <see cref="VersionTag.ToWire" /> under the format version of the shape
    ///         the tag covers, so a client refetches when either the data or its shape changes. The
    ///         client's permissions tag is a composite of the tenant's <c>permissions</c> tag and the
    ///         user's own permissions tag, so a change of either reaches the user's browser.
    ///     </para>
    ///     <para>
    ///         <c>entity:*</c> tags never travel here, since their number is unbounded: a changed
    ///         <c>entities</c> tag tells the client to ask which lists moved.
    ///     </para>
    /// </remarks>
    public static class VersionTagWire
    {
        /// <summary>
        ///     The format version of the caller's profile payload, which the <c>preferences</c> and
        ///     <c>permissions</c> wire tags cover. Raise it whenever that payload's shape changes, so
        ///     every client refetches.
        /// </summary>
        public const int MeFormatVersion = 1;

        /// <summary>The header item naming the <c>settings</c> tag.</summary>
        public const string SettingsItem = VersionTagNames.Settings;

        /// <summary>The header item naming the caller's composite permissions tag.</summary>
        public const string PermissionsItem = VersionTagNames.Permissions;

        /// <summary>The header item naming the caller's preferences tag.</summary>
        public const string PreferencesItem = "preferences";

        /// <summary>The header item naming the <c>entities</c> tag.</summary>
        public const string EntitiesItem = VersionTagNames.Entities;

        /// <summary>The wire form of the <c>settings</c> tag, under the client settings' format version.</summary>
        /// <param name="settings">The tenant's <c>settings</c> tag.</param>
        /// <returns><c>"{format}.{tag:N}"</c>.</returns>
        public static string Settings(VersionTag settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return settings.ToWire(TenantSettingsForClient.FormatVersion);
        }

        /// <summary>The wire form of the <c>entities</c> tag, or of one <c>entity:*</c> tag, under the cached lists' format version.</summary>
        /// <param name="entities">The tag.</param>
        /// <returns><c>"{format}.{tag:N}"</c>.</returns>
        public static string Entities(VersionTag entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            return entities.ToWire(CachedEntitySet<object>.FormatVersion);
        }

        /// <summary>The wire form of a user's preferences tag, under the profile payload's format version.</summary>
        /// <param name="preferences">The user's preferences tag.</param>
        /// <returns><c>"{format}.{tag:N}"</c>.</returns>
        public static string Preferences(VersionTag preferences)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            return preferences.ToWire(MeFormatVersion);
        }

        /// <summary>
        ///     The client's composite permissions tag: the tenant's <c>permissions</c> tag and the
        ///     user's own permissions tag under the profile payload's format version, so a role edit
        ///     and a membership edit both change it.
        /// </summary>
        /// <param name="tenantPermissions">The tenant's <c>permissions</c> tag.</param>
        /// <param name="userPermissions">The user's own permissions tag.</param>
        /// <returns><c>"{format}.{tenant:N}{user:N}"</c>.</returns>
        public static string Permissions(VersionTag tenantPermissions, VersionTag userPermissions)
        {
            ArgumentNullException.ThrowIfNull(tenantPermissions);
            ArgumentNullException.ThrowIfNull(userPermissions);
            return string.Create(CultureInfo.InvariantCulture, $"{MeFormatVersion}.{tenantPermissions.Value:N}{userPermissions.Value:N}");
        }

        /// <summary>
        ///     The value of the response header that carries the four client tags. A response without
        ///     the caller's own tags (a batch that ran for the platform or for no caller) omits
        ///     <c>permissions</c> and <c>preferences</c>.
        /// </summary>
        /// <param name="tenant">The tenant's tags after the request's last batch.</param>
        /// <param name="user">The caller's own tags after that batch, or null when it carried none.</param>
        /// <returns>The header value, items in the order <c>settings</c>, <c>permissions</c>, <c>preferences</c>, <c>entities</c>.</returns>
        public static string HeaderValue(VersionTagSnapshot tenant, UserVersionTagSnapshot? user)
        {
            ArgumentNullException.ThrowIfNull(tenant);

            StringBuilder header = new();
            Item(header, SettingsItem, Settings(tenant[VersionTagNames.Settings]));
            if (user is not null)
            {
                Item(header, PermissionsItem, Permissions(tenant[VersionTagNames.Permissions], user.Permissions));
                Item(header, PreferencesItem, Preferences(user.Preferences));
            }

            Item(header, EntitiesItem, Entities(tenant[VersionTagNames.Entities]));
            return header.ToString();
        }

        /// <summary>Appends one <c>name=value</c> item, comma-separated from the previous one.</summary>
        /// <param name="header">The header so far.</param>
        /// <param name="name">The item's name.</param>
        /// <param name="value">The item's value.</param>
        private static void Item(StringBuilder header, string name, string value)
        {
            if (header.Length > 0)
            {
                _ = header.Append(", ");
            }

            _ = header.Append(name).Append('=').Append(value);
        }
    }
}
