// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Settings
{
    // The field mask is spec 0013 §6.3's: a patch lists the typed members it sets by name and the
    // entries as "entries.<key>"; anything else is Settings.FieldUnknown at its position. A typed
    // member's name compares without regard to case, as the wire's camelCase spelling of it would;
    // a key compares ordinally, as the registry does.
    /// <summary>A patch's field mask, sorted into the typed members and the entry keys it lists, and the fields that name neither.</summary>
    internal sealed class SettingsPatchFields
    {
        /// <summary>The typed members a patch may list, as the typed row spells them.</summary>
        public static readonly FrozenSet<string> TypedMembers = new[]
        {
            nameof(TenantSettingsPatch.Name),
            nameof(TenantSettingsPatch.Name2),
            nameof(TenantSettingsPatch.Name3),
            nameof(TenantSettingsPatch.PrimaryLanguage),
            nameof(TenantSettingsPatch.SecondaryLanguage),
            nameof(TenantSettingsPatch.TertiaryLanguage),
            nameof(TenantSettingsPatch.PrimaryCalendar),
            nameof(TenantSettingsPatch.SecondaryCalendar),
            nameof(TenantSettingsPatch.TimeZone),
            nameof(TenantSettingsPatch.Region),
            nameof(TenantSettingsPatch.Digits),
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>Creates the sorted mask.</summary>
        /// <param name="typed">The typed members listed.</param>
        /// <param name="keys">The entry keys listed, in order.</param>
        /// <param name="unknown">The positions of the fields that name neither.</param>
        private SettingsPatchFields(FrozenSet<string> typed, IReadOnlyList<string> keys, IReadOnlyList<int> unknown)
        {
            Typed = typed;
            Keys = keys;
            Unknown = unknown;
        }

        /// <summary>The typed members listed, as the typed row spells them.</summary>
        public FrozenSet<string> Typed { get; }

        /// <summary>Whether any typed member is listed, so the typed row is read, checked and saved.</summary>
        public bool ListsTyped => Typed.Count > 0;

        /// <summary>The entry keys listed, each once, in the order the mask first lists them.</summary>
        public IReadOnlyList<string> Keys { get; }

        /// <summary>The positions in the mask of the fields that name no typed member and no entry.</summary>
        public IReadOnlyList<int> Unknown { get; }

        /// <summary>Sorts a patch's field mask.</summary>
        /// <param name="patch">The patch.</param>
        /// <returns>The sorted mask.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="patch" /> is null.</exception>
        public static SettingsPatchFields Of(TenantSettingsPatch patch)
        {
            ArgumentNullException.ThrowIfNull(patch);
            HashSet<string> typed = new(StringComparer.Ordinal);
            List<string> keys = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<int> unknown = [];
            IReadOnlyList<string> fields = patch.Fields ?? [];
            for (int index = 0; index < fields.Count; index++)
            {
                string? field = fields[index];

                // --- A typed member, by its own spelling ---
                if (field is not null && TypedMembers.TryGetValue(field, out string? member))
                {
                    _ = typed.Add(member);
                    continue;
                }

                // --- An entry: the prefix, then a key ---
                if (field is not null
                    && field.Length > TenantSettingsPatch.EntryFieldPrefix.Length
                    && field.StartsWith(TenantSettingsPatch.EntryFieldPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string key = field[TenantSettingsPatch.EntryFieldPrefix.Length..];
                    if (seen.Add(key))
                    {
                        keys.Add(key);
                    }

                    continue;
                }

                unknown.Add(index);
            }

            return new SettingsPatchFields(typed.ToFrozenSet(StringComparer.Ordinal), keys.AsReadOnly(), unknown.AsReadOnly());
        }
    }
}
