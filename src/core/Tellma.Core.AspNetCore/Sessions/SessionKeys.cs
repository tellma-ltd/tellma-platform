// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     A session's two names. The key is the credential: 256 random bits, carried by the session
    ///     cookie and known to the store alone. The handle is the name everything else uses — the
    ///     request context, the listeners, the profile cookie — derived one way from the key, so knowing
    ///     a handle never yields the key.
    /// </summary>
    internal static class SessionKeys
    {
        /// <summary>The length of a key: 32 bytes in unpadded base64url.</summary>
        public const int KeyLength = 43;

        /// <summary>The length of a handle: 16 bytes in unpadded base64url.</summary>
        public const int HandleLength = 22;

        /// <summary>The bytes of randomness in a key.</summary>
        private const int KeyBytes = 32;

        /// <summary>The bytes of the key's digest a handle keeps.</summary>
        private const int HandleBytes = 16;

        /// <summary>A new key from the operating system's cryptographic generator.</summary>
        /// <returns>The key, 43 base64url characters.</returns>
        public static string NewKey()
        {
            return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeyBytes));
        }

        /// <summary>The handle of a key: the first 16 bytes of the SHA-256 of its characters, in base64url.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The handle, 22 base64url characters.</returns>
        public static string HandleOf(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            _ = SHA256.HashData(Encoding.ASCII.GetBytes(key), digest);
            return Base64Url.EncodeToString(digest[..HandleBytes]);
        }

        /// <summary>Whether a value has a key's shape: 43 base64url characters.</summary>
        /// <param name="value">The value, as a cookie carried it.</param>
        /// <returns>True when it could be a key.</returns>
        public static bool IsWellFormedKey(string? value)
        {
            return value is { Length: KeyLength } && value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
        }
    }
}
