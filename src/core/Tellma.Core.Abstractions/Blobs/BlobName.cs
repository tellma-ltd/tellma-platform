// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     The grammar of blob kinds, variants and object names:
    ///     <c>{kind}/{k0k1}/{key32}[.{variant}]</c>, where <c>key32</c> is 32 lowercase hexadecimal
    ///     digits and <c>k0k1</c> its first two, which fans a kind's objects out over 256 segments.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every store validates every name through <see cref="IsValid" /> before it touches
    ///         storage. The grammar admits only lowercase ASCII letters, digits, <c>-</c>, one
    ///         <c>.</c> and exactly two <c>/</c>, so no valid name can contain a traversal segment, a
    ///         drive, a backslash, a rooted path or a character whose meaning differs between file
    ///         systems — and, being lowercase, no two valid names differ only in case, which keeps
    ///         case-insensitive and case-sensitive stores in agreement.
    ///     </para>
    ///     <para>
    ///         A kind is also refused when it is a device name Windows reserves in every directory
    ///         (<c>con</c>, <c>prn</c>, <c>aux</c>, <c>nul</c>, <c>com0</c>–<c>com9</c>,
    ///         <c>lpt0</c>–<c>lpt9</c>), because the file-system store makes each kind a directory and
    ///         such a directory cannot be created reliably on Windows.
    ///     </para>
    /// </remarks>
    public static class BlobName
    {
        /// <summary>The shortest valid kind.</summary>
        public const int MinKindLength = 2;

        /// <summary>The longest valid kind.</summary>
        public const int MaxKindLength = 40;

        /// <summary>The longest valid variant.</summary>
        public const int MaxVariantLength = 16;

        /// <summary>The length of a storage key: a GUID's 32 lowercase hexadecimal digits.</summary>
        public const int KeyLength = 32;

        /// <summary>The length of the fan-out segment: the key's first two digits.</summary>
        public const int FanOutLength = 2;

        /// <summary>The longest valid object name: a maximal kind, fan-out, key and variant.</summary>
        public const int MaxNameLength = MaxKindLength + 1 + FanOutLength + 1 + KeyLength + 1 + MaxVariantLength;

        /// <summary>The separator between a primary name and its variant.</summary>
        public const char VariantSeparator = '.';

        /// <summary>The separator between the segments of a name.</summary>
        public const char SegmentSeparator = '/';

        // The Windows device names, lowercase as the kind grammar demands. Windows reserves them in
        // every directory and with any extension, so a kind directory with one of these names is
        // either refused or silently opens the device.
        private static readonly HashSet<string> ReservedKinds = new(StringComparer.Ordinal)
        {
            "con", "prn", "aux", "nul",
            "com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
            "lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
        };

        /// <summary>
        ///     Whether <paramref name="kind" /> is a valid blob kind: <c>^[a-z][a-z0-9-]{1,39}$</c>,
        ///     and not a reserved Windows device name.
        /// </summary>
        /// <param name="kind">The candidate kind.</param>
        /// <returns>True when the kind is valid; false for null.</returns>
        public static bool IsValidKind([NotNullWhen(true)] string? kind)
        {
            return kind is not null && MatchesKindGrammar(kind) && !ReservedKinds.Contains(kind);
        }

        /// <summary>Whether <paramref name="variant" /> is a valid variant name: <c>^[a-z0-9]{1,16}$</c>.</summary>
        /// <param name="variant">The candidate variant.</param>
        /// <returns>True when the variant is valid; false for null.</returns>
        public static bool IsValidVariant([NotNullWhen(true)] string? variant)
        {
            return variant is not null && MatchesVariantGrammar(variant);
        }

        /// <summary>
        ///     Whether <paramref name="key32" /> is a valid storage key: 32 lowercase hexadecimal
        ///     digits (<c>^[0-9a-f]{32}$</c>), the form <c>Guid.ToString("N")</c> produces.
        /// </summary>
        /// <param name="key32">The candidate key.</param>
        /// <returns>True when the key is valid; false for null.</returns>
        public static bool IsValidKey([NotNullWhen(true)] string? key32)
        {
            return key32 is not null && key32.Length == KeyLength && IsLowerHex(key32);
        }

        /// <summary>
        ///     Whether <paramref name="name" /> is a valid object name: a primary name
        ///     <c>{kind}/{k0k1}/{key32}</c> whose fan-out segment is its key's first two digits,
        ///     optionally followed by <c>.{variant}</c>.
        /// </summary>
        /// <param name="name">The candidate name.</param>
        /// <returns>True when the name is valid; false for null.</returns>
        public static bool IsValid([NotNullWhen(true)] string? name)
        {
            return name is not null && TrySplit(name, out _);
        }

        /// <summary>Whether <paramref name="name" /> is a valid primary name, one with no variant.</summary>
        /// <param name="name">The candidate name.</param>
        /// <returns>True when the name is valid and names a primary; false for null.</returns>
        public static bool IsValidPrimary([NotNullWhen(true)] string? name)
        {
            return name is not null && TrySplit(name, out bool hasVariant) && !hasVariant;
        }

        /// <summary>
        ///     Whether <paramref name="prefix" /> is a valid listing prefix: a kind
        ///     (<c>user-image</c>), or a kind and a fan-out segment (<c>user-image/3f</c>).
        /// </summary>
        /// <remarks>
        ///     A prefix addresses whole segments: <c>user-image</c> lists the objects of that kind
        ///     only, never those of a kind that merely starts with the same letters.
        /// </remarks>
        /// <param name="prefix">The candidate prefix.</param>
        /// <returns>True when the prefix is valid; false for null.</returns>
        public static bool IsValidPrefix([NotNullWhen(true)] string? prefix)
        {
            if (prefix is null)
            {
                return false;
            }

            int slash = prefix.IndexOf(SegmentSeparator, StringComparison.Ordinal);
            if (slash < 0)
            {
                return IsValidKind(prefix);
            }

            ReadOnlySpan<char> fanOut = prefix.AsSpan(slash + 1);
            return IsValidKind(prefix[..slash]) && fanOut.Length == FanOutLength && IsLowerHex(fanOut);
        }

        /// <summary>The primary object name of a blob: <c>{kind}/{k0k1}/{key32}</c>.</summary>
        /// <param name="kind">The blob's kind; it must satisfy <see cref="IsValidKind" />.</param>
        /// <param name="key32">The blob's storage key; it must satisfy <see cref="IsValidKey" />.</param>
        /// <returns>The primary name.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">The kind or the key is outside its grammar.</exception>
        public static string Primary(string kind, string key32)
        {
            ArgumentNullException.ThrowIfNull(kind);
            ArgumentNullException.ThrowIfNull(key32);

            ThrowIfInvalid(IsValidKind(kind), $"'{kind}' is not a valid blob kind.", nameof(kind));
            return IsValidKey(key32)
                ? $"{kind}{SegmentSeparator}{key32[..FanOutLength]}{SegmentSeparator}{key32}"
                : throw new ArgumentException("A blob storage key is 32 lowercase hexadecimal digits.", nameof(key32));
        }

        /// <summary>The object name of a variant of a blob: <c>{primaryName}.{variant}</c>.</summary>
        /// <param name="primaryName">The blob's primary name; it must satisfy <see cref="IsValidPrimary" />.</param>
        /// <param name="variant">The variant; it must satisfy <see cref="IsValidVariant" />.</param>
        /// <returns>The variant's object name.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">The primary name or the variant is outside its grammar.</exception>
        public static string Variant(string primaryName, string variant)
        {
            ArgumentNullException.ThrowIfNull(primaryName);
            ArgumentNullException.ThrowIfNull(variant);

            ThrowIfInvalid(IsValidPrimary(primaryName), $"'{primaryName}' is not a valid primary blob name.", nameof(primaryName));
            return IsValidVariant(variant)
                ? $"{primaryName}{VariantSeparator}{variant}"
                : throw new ArgumentException($"'{variant}' is not a valid blob variant.", nameof(variant));
        }

        /// <summary>Throws <see cref="ArgumentException" /> unless <paramref name="valid" />.</summary>
        /// <param name="valid">Whether the argument is valid.</param>
        /// <param name="message">The exception's message.</param>
        /// <param name="parameterName">The argument's name.</param>
        /// <exception cref="ArgumentException"><paramref name="valid" /> is false.</exception>
        private static void ThrowIfInvalid(bool valid, string message, string parameterName)
        {
            if (!valid)
            {
                throw new ArgumentException(message, parameterName);
            }
        }

        /// <summary>Splits and validates a name.</summary>
        /// <param name="name">The candidate name.</param>
        /// <param name="hasVariant">Whether the name carries a variant, when valid.</param>
        /// <returns>True when the name is valid.</returns>
        private static bool TrySplit(string name, out bool hasVariant)
        {
            hasVariant = false;

            // The bound keeps a hostile name from costing more than a fixed scan.
            if (name.Length > MaxNameLength)
            {
                return false;
            }

            // {kind}/{k0k1}/{key32}[.{variant}]: exactly two slashes, the first ending the kind.
            int first = name.IndexOf(SegmentSeparator, StringComparison.Ordinal);
            if (first < 0)
            {
                return false;
            }

            int second = name.IndexOf(SegmentSeparator, first + 1);
            if (second < 0 || name.IndexOf(SegmentSeparator, second + 1) >= 0)
            {
                return false;
            }

            string candidateKind = name[..first];
            ReadOnlySpan<char> fanOut = name.AsSpan(first + 1, second - first - 1);
            ReadOnlySpan<char> tail = name.AsSpan(second + 1);

            // The tail is the key, then optionally one '.' and the variant.
            ReadOnlySpan<char> key = tail;
            int dot = tail.IndexOf(VariantSeparator);
            if (dot >= 0)
            {
                key = tail[..dot];
                if (!MatchesVariantGrammar(tail[(dot + 1)..]))
                {
                    return false;
                }

                hasVariant = true;
            }

            // The fan-out segment must be the key's own first two digits, so one blob has exactly
            // one spelling of its name.
            return IsValidKind(candidateKind)
                && key.Length == KeyLength
                && IsLowerHex(key)
                && fanOut.SequenceEqual(key[..FanOutLength]);
        }

        /// <summary>Matches <c>^[a-z][a-z0-9-]{1,39}$</c>.</summary>
        /// <param name="kind">The candidate kind.</param>
        /// <returns>True when it matches.</returns>
        private static bool MatchesKindGrammar(ReadOnlySpan<char> kind)
        {
            if (kind.Length is < MinKindLength or > MaxKindLength || kind[0] is not (>= 'a' and <= 'z'))
            {
                return false;
            }

            foreach (char c in kind[1..])
            {
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Matches <c>^[a-z0-9]{1,16}$</c>.</summary>
        /// <param name="variant">The candidate variant.</param>
        /// <returns>True when it matches.</returns>
        private static bool MatchesVariantGrammar(ReadOnlySpan<char> variant)
        {
            if (variant.Length is < 1 or > MaxVariantLength)
            {
                return false;
            }

            foreach (char c in variant)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9')))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether every character is a lowercase hexadecimal digit.</summary>
        /// <param name="value">The characters.</param>
        /// <returns>True when all are; true for an empty span.</returns>
        private static bool IsLowerHex(ReadOnlySpan<char> value)
        {
            foreach (char c in value)
            {
                if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
