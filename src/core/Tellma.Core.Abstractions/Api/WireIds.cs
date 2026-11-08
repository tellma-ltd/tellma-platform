// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Narrows the ids a request carries to a stack's key type. Request records hold ids as
    ///     <see cref="long" />, whatever the key, because an id arrives as a JSON number or string;
    ///     whoever holds the stack's key type narrows them here before calling the service.
    /// </summary>
    /// <remarks>
    ///     A stack's key type is <see cref="int" /> or <see cref="long" />. An id outside an
    ///     <see cref="int" /> key's range is one bad-request error naming its path, never a silent
    ///     truncation.
    /// </remarks>
    public static class WireIds
    {
        /// <summary>Narrows a list of ids to the key type.</summary>
        /// <typeparam name="TKey">The stack's key type: <see cref="int" /> or <see cref="long" />.</typeparam>
        /// <param name="ids">The ids, in request order.</param>
        /// <param name="path">The request member that carried them, such as <c>ids</c>; an error names <c>ids[i]</c>.</param>
        /// <returns>The ids as keys, in the same order.</returns>
        /// <exception cref="BadRequestException">An id is outside the key type's range.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="ids" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="path" /> is null, empty or white space.</exception>
        /// <exception cref="NotSupportedException"><typeparamref name="TKey" /> is neither <see cref="int" /> nor <see cref="long" />.</exception>
        public static IReadOnlyList<TKey> ToKeys<TKey>(IReadOnlyList<long> ids, string path)
            where TKey : struct
        {
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            // A long key needs no narrowing: the request's own list is the answer.
            if (typeof(TKey) == typeof(long))
            {
                return (IReadOnlyList<TKey>)(object)ids;
            }

            if (typeof(TKey) == typeof(int))
            {
                int[] keys = new int[ids.Count];
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i] = Narrow(ids[i], string.Create(CultureInfo.InvariantCulture, $"{path}[{i}]"));
                }

                return (IReadOnlyList<TKey>)(object)Array.AsReadOnly(keys);
            }

            throw Unsupported<TKey>();
        }

        /// <summary>Narrows one id to the key type.</summary>
        /// <typeparam name="TKey">The stack's key type: <see cref="int" /> or <see cref="long" />.</typeparam>
        /// <param name="id">The id.</param>
        /// <param name="path">The request member that carried it, such as <c>id</c>.</param>
        /// <returns>The id as a key.</returns>
        /// <exception cref="BadRequestException">The id is outside the key type's range.</exception>
        /// <exception cref="ArgumentException"><paramref name="path" /> is null, empty or white space.</exception>
        /// <exception cref="NotSupportedException"><typeparamref name="TKey" /> is neither <see cref="int" /> nor <see cref="long" />.</exception>
        public static TKey ToKey<TKey>(long id, string path)
            where TKey : struct
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            return typeof(TKey) == typeof(long)
                ? (TKey)(object)id
                : typeof(TKey) == typeof(int)
                    ? (TKey)(object)Narrow(id, path)
                    : throw Unsupported<TKey>();
        }

        /// <summary>Narrows one id to an <see cref="int" /> key, or refuses it naming its path.</summary>
        /// <param name="id">The id.</param>
        /// <param name="path">The id's path in the request.</param>
        /// <returns>The key.</returns>
        private static int Narrow(long id, string path)
        {
            return id is >= int.MinValue and <= int.MaxValue
                ? (int)id
                : throw new BadRequestException(
                    $"The id at '{path}' is outside the range of the resource's key.",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = path });
        }

        /// <summary>The refusal of a key type no stack has.</summary>
        /// <typeparam name="TKey">The key type.</typeparam>
        /// <returns>The exception to throw.</returns>
        private static NotSupportedException Unsupported<TKey>()
        {
            return new NotSupportedException($"A stack's key type is int or long; '{typeof(TKey).Name}' is neither.");
        }
    }
}
