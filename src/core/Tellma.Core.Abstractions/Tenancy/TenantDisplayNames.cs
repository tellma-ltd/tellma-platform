// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     A tenant's display names and content-language codes as its settings hold them, which the
    ///     catalog mirrors so the tenant picker can show the tenant without opening its database.
    /// </summary>
    /// <remarks>
    ///     Each name is paired with the code at the same position of <see cref="Languages" />:
    ///     <see cref="Name" /> with the first, <see cref="Name2" /> with the second and
    ///     <see cref="Name3" /> with the third. A name is null exactly when its language is absent.
    ///     Two values are equal when every member is, <see cref="Languages" /> compared element by
    ///     element.
    /// </remarks>
    /// <param name="Name">The name in the first content language.</param>
    /// <param name="Name2">The name in the second content language, or null when there is none.</param>
    /// <param name="Name3">The name in the third content language, or null when there is none.</param>
    /// <param name="Languages">The content-language codes, first to third; one to three entries.</param>
    public sealed record TenantDisplayNames(string Name, string? Name2, string? Name3, IReadOnlyList<string> Languages)
    {
        /// <inheritdoc cref="TenantDisplayNames(string, string?, string?, IReadOnlyList{string})" />
        public string Name { get; init; } = Name ?? throw new ArgumentNullException(nameof(Name));

        /// <summary>
        ///     The content-language codes, first to third: a read-only copy of the list the value was
        ///     created with. Get-only, so a <c>with</c> expression cannot replace the copy.
        /// </summary>
        public IReadOnlyList<string> Languages { get; } = ReadOnlyCopies.OfEntries(Languages, requireOne: true, nameof(Languages));

        /// <inheritdoc />
        public bool Equals(TenantDisplayNames? other)
        {
            return other is not null
                && string.Equals(Name, other.Name, StringComparison.Ordinal)
                && string.Equals(Name2, other.Name2, StringComparison.Ordinal)
                && string.Equals(Name3, other.Name3, StringComparison.Ordinal)
                && Languages.SequenceEqual(other.Languages, StringComparer.Ordinal);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(Name, StringComparer.Ordinal);
            hash.Add(Name2, StringComparer.Ordinal);
            hash.Add(Name3, StringComparer.Ordinal);
            foreach (string language in Languages)
            {
                hash.Add(language, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
