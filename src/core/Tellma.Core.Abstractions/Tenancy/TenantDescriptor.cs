// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     What the platform shows about a tenant without opening its database: its id, its display
    ///     names in up to three content languages, its category and state, and the live tenant a
    ///     sandbox derives from. It never carries the tenant's database location.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The names mirror the tenant settings' name group and <see cref="Languages" /> their
    ///         content-language codes, in order: <see cref="Name" /> is in the first language,
    ///         <see cref="Name2" /> and <see cref="Name3" /> in the second and third, and each is null
    ///         when the tenant has no such language. Until the tenant's settings are first saved,
    ///         <see cref="Languages" /> holds the distribution's default language alone.
    ///     </para>
    ///     <para>
    ///         Two descriptors are equal when every member is, <see cref="Languages" /> compared
    ///         element by element, so a refreshed descriptor that changed nothing compares equal to the
    ///         one it replaces.
    ///     </para>
    /// </remarks>
    /// <param name="Id">The tenant's id: a positive integer, the leading segment of every tenant route.</param>
    /// <param name="Name">The display name in the first content language.</param>
    /// <param name="Name2">The display name in the second content language, or null when there is none.</param>
    /// <param name="Name3">The display name in the third content language, or null when there is none.</param>
    /// <param name="Languages">The content-language codes, first to third; one to three entries.</param>
    /// <param name="Category">Whether the tenant is live or a sandbox.</param>
    /// <param name="State">The tenant's lifecycle state.</param>
    /// <param name="LiveTenantId">The live tenant a sandbox derives from; null for a live tenant and a standalone sandbox.</param>
    public sealed record TenantDescriptor(
        int Id,
        string Name,
        string? Name2,
        string? Name3,
        IReadOnlyList<string> Languages,
        TenantCategory Category,
        TenantState State,
        int? LiveTenantId)
    {
        /// <inheritdoc cref="TenantDescriptor(int, string, string?, string?, IReadOnlyList{string}, TenantCategory, TenantState, int?)" />
        public string Name { get; init; } = Name ?? throw new ArgumentNullException(nameof(Name));

        /// <summary>
        ///     The content-language codes, first to third: a read-only copy of the list the descriptor
        ///     was created with. Get-only, so a <c>with</c> expression cannot replace the copy with a
        ///     list its caller still holds.
        /// </summary>
        public IReadOnlyList<string> Languages { get; } = ReadOnlyCopies.OfEntries(Languages, requireOne: true, nameof(Languages));

        /// <inheritdoc />
        public bool Equals(TenantDescriptor? other)
        {
            return other is not null
                && Id == other.Id
                && string.Equals(Name, other.Name, StringComparison.Ordinal)
                && string.Equals(Name2, other.Name2, StringComparison.Ordinal)
                && string.Equals(Name3, other.Name3, StringComparison.Ordinal)
                && Languages.SequenceEqual(other.Languages, StringComparer.Ordinal)
                && Category == other.Category
                && State == other.State
                && LiveTenantId == other.LiveTenantId;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(Id);
            hash.Add(Name, StringComparer.Ordinal);
            hash.Add(Name2, StringComparer.Ordinal);
            hash.Add(Name3, StringComparer.Ordinal);
            foreach (string language in Languages)
            {
                hash.Add(language, StringComparer.Ordinal);
            }

            hash.Add(Category);
            hash.Add(State);
            hash.Add(LiveTenantId);
            return hash.ToHashCode();
        }
    }
}
