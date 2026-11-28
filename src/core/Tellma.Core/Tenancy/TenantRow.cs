// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     One <c>catalog.Tenants</c> row as the snapshot reload reads it: the columns of the reload
    ///     statement, in its order, with the category and state still as stored.
    /// </summary>
    /// <param name="Id">The tenant id.</param>
    /// <param name="Name">The first name.</param>
    /// <param name="Name2">The second name.</param>
    /// <param name="Name3">The third name.</param>
    /// <param name="Language1">The first content-language code.</param>
    /// <param name="Language2">The second content-language code.</param>
    /// <param name="Language3">The third content-language code.</param>
    /// <param name="Category">The stored category name.</param>
    /// <param name="LiveTenantId">The live tenant a sandbox derives from.</param>
    /// <param name="State">The stored state name.</param>
    /// <param name="Server">The database server.</param>
    /// <param name="Database">The database name.</param>
    /// <param name="CredentialProfile">The profile name.</param>
    /// <param name="Properties">The non-secret JSON.</param>
    /// <param name="Version">The row version.</param>
    internal sealed record TenantRow(
        int Id,
        string Name,
        string? Name2,
        string? Name3,
        string Language1,
        string? Language2,
        string? Language3,
        string Category,
        int? LiveTenantId,
        string State,
        string Server,
        string Database,
        string CredentialProfile,
        string? Properties,
        Guid Version)
    {
        /// <summary>
        ///     The row's descriptor, or null when its category or state is not one this runtime knows,
        ///     which only a catalog written by a newer platform could hold.
        /// </summary>
        /// <returns>The descriptor, or null.</returns>
        public TenantDescriptor? ToDescriptor()
        {
            if (!TryParseCategory(Category, out TenantCategory category) || !TryParseState(State, out TenantState state))
            {
                return null;
            }

            // The catalog pairs each name with its code, but not the second pair with the third, so a
            // row written outside the catalog's own statements could hold a third language without a
            // second. Positions are what pair names with codes, so such a third pair is dropped rather
            // than shifted into the second position.
            List<string> languages = [Language1];
            string? name2 = null;
            string? name3 = null;
            if (Language2 is not null)
            {
                languages.Add(Language2);
                name2 = Name2;
                if (Language3 is not null)
                {
                    languages.Add(Language3);
                    name3 = Name3;
                }
            }

            return new TenantDescriptor(Id, Name, name2, name3, languages, category, state, LiveTenantId);
        }

        /// <summary>Reads the row at the reader's current position, in the reload statement's column order.</summary>
        /// <param name="reader">The reader.</param>
        /// <returns>The row.</returns>
        public static TenantRow Read(SqlDataReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            return new TenantRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetGuid(14));
        }

        /// <summary>Parses a stored category name exactly; numbers and other spellings are refused.</summary>
        /// <param name="value">The stored name.</param>
        /// <param name="category">The category.</param>
        /// <returns>True when the name is a category's.</returns>
        public static bool TryParseCategory(string value, out TenantCategory category)
        {
            (bool known, category) = value switch
            {
                nameof(TenantCategory.Live) => (true, TenantCategory.Live),
                nameof(TenantCategory.Sandbox) => (true, TenantCategory.Sandbox),
                _ => (false, default),
            };

            return known;
        }

        /// <summary>Parses a stored state name exactly; numbers and other spellings are refused.</summary>
        /// <param name="value">The stored name.</param>
        /// <param name="state">The state.</param>
        /// <returns>True when the name is a state's.</returns>
        public static bool TryParseState(string value, out TenantState state)
        {
            (bool known, state) = value switch
            {
                nameof(TenantState.Provisioning) => (true, TenantState.Provisioning),
                nameof(TenantState.Active) => (true, TenantState.Active),
                nameof(TenantState.ReadOnly) => (true, TenantState.ReadOnly),
                nameof(TenantState.Frozen) => (true, TenantState.Frozen),
                nameof(TenantState.Suspended) => (true, TenantState.Suspended),
                nameof(TenantState.Retired) => (true, TenantState.Retired),
                _ => (false, default),
            };

            return known;
        }
    }
}
