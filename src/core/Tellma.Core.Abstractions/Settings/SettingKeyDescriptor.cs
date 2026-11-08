// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     A setting key as the settings editor needs it, written into the client metadata so the
    ///     editor renders every tab before any request.
    /// </summary>
    /// <param name="Name">The key's name (<c>gl.posting.autoNumberOnPost</c>).</param>
    /// <param name="Category">The key's category: the settings tab it appears on.</param>
    /// <param name="Group">The card inside the category: the key's stated group, else the one its name derives (<see cref="SettingKey.DeriveGroup" />); null for none.</param>
    /// <param name="Visibility">Who may see the key's value.</param>
    /// <param name="ValueType">The kind of value; <see cref="SettingValueType.Json" /> for a key with its own serialization contract.</param>
    /// <param name="IsNullable">Whether the value may be null.</param>
    /// <param name="IsArray">Whether the value is an array of <paramref name="ValueType" />.</param>
    /// <param name="EnumType">The enum's name, for <see cref="SettingValueType.Enum" />; else null.</param>
    /// <param name="EnumValues">The enum's member names in declaration order, for <see cref="SettingValueType.Enum" />; else null.</param>
    /// <param name="References">The qualified name of the entity an id value points at; else null.</param>
    /// <param name="DefaultJson">The code default, serialized; JSON null for a secret key.</param>
    /// <param name="LabelKey">The label's resource key, <c>Setting_</c> and the name with each <c>.</c> as <c>_</c>.</param>
    /// <param name="DescriptionKey">The description's resource key, <c>Setting_…_Description</c>, only when a composed resource declares it; else null.</param>
    public sealed record SettingKeyDescriptor(
        string Name,
        string Category,
        string? Group,
        SettingVisibility Visibility,
        SettingValueType ValueType,
        bool IsNullable,
        bool IsArray,
        string? EnumType,
        IReadOnlyList<string>? EnumValues,
        string? References,
        JsonElement DefaultJson,
        string LabelKey,
        string? DescriptionKey);
}
