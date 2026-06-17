// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Content-addressed naming for table types (spec 0001 §3 → Versioning). A type's deployed
    ///     <b>physical name</b> is its <b>logical name</b> plus an underscore and the first eight hex
    ///     characters of the SHA-256 of its canonical-JSON definition. Equal definitions hash to the
    ///     same physical name; any definitional change yields a different one, so a new version is
    ///     created alongside the old rather than mutating a shared name — which is what keeps an
    ///     N−1 app safe through a deployment window (TVP binding is positional and name-blind).
    /// </summary>
    /// <remarks>
    ///     The hash is always derived from the definition JSON and never stored inside it, so it
    ///     cannot drift from the content it names. The full hash is also stamped on the created type
    ///     (see <see cref="TableTypeStampNames.DefinitionHash" />) so the idempotent create can tell
    ///     a content collision (wrong bytes at a name) from a mere ownership conflict.
    /// </remarks>
    public static class TableTypeNaming
    {
        /// <summary>The number of hex characters of the definition hash appended to the logical name.</summary>
        public const int HashSuffixLength = 8;

        /// <summary>
        ///     The longest a logical name may be so that the physical name (logical + <c>'_'</c> +
        ///     <see cref="HashSuffixLength" /> hex chars) fits SQL Server's 128-character identifier
        ///     limit.
        /// </summary>
        public const int MaxLogicalNameLength = 128 - HashSuffixLength - 1;

        /// <summary>
        ///     Computes the full lower-case hex SHA-256 of a definition's canonical JSON. This is the
        ///     value stamped as <see cref="TableTypeStampNames.DefinitionHash" />.
        /// </summary>
        /// <param name="canonicalJson">The canonical JSON of the definition (see <see cref="TableTypeJson" />).</param>
        /// <returns>The 64-character lower-case hex hash.</returns>
        public static string ComputeHash(string canonicalJson)
        {
            ArgumentNullException.ThrowIfNull(canonicalJson);

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
            return Convert.ToHexStringLower(hash);
        }

        /// <summary>
        ///     Computes the physical name (<c>&lt;logicalName&gt;_&lt;hash8&gt;</c>) for a definition
        ///     from its logical name and the full definition hash.
        /// </summary>
        /// <param name="logicalName">The configured (logical) name of the type.</param>
        /// <param name="fullHash">The full hash from <see cref="ComputeHash(string)" />.</param>
        /// <returns>The deployed physical name.</returns>
        public static string PhysicalName(string logicalName, string fullHash)
        {
            ArgumentException.ThrowIfNullOrEmpty(logicalName);
            ArgumentException.ThrowIfNullOrEmpty(fullHash);

            return string.Concat(logicalName, "_", fullHash.AsSpan(0, HashSuffixLength));
        }

        /// <summary>
        ///     Computes both the full hash and the physical name for a definition given its canonical
        ///     JSON and logical name — the form the differ uses when it already holds the annotation
        ///     string verbatim.
        /// </summary>
        /// <param name="logicalName">The configured (logical) name of the type.</param>
        /// <param name="canonicalJson">The canonical JSON annotation value of the definition.</param>
        /// <returns>The full hash and the physical name.</returns>
        public static (string FullHash, string PhysicalName) Resolve(string logicalName, string canonicalJson)
        {
            string fullHash = ComputeHash(canonicalJson);
            return (fullHash, PhysicalName(logicalName, fullHash));
        }

        /// <summary>
        ///     Formats an invariant-culture string of an integer, for embedding error numbers and
        ///     grace periods in generated SQL.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The invariant string.</returns>
        internal static string Invariant(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
