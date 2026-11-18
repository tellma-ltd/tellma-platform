// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Crud.Details
{
    // The declarations are spec 0015 §2.2's [DetailsExtras], read the way the descriptor reads them.
    /// <summary>The details extras a producer class declares: the names on it and on its base classes, read once per class.</summary>
    internal static class DetailsExtrasNames
    {
        /// <summary>The names, by class.</summary>
        private static readonly ConcurrentDictionary<Type, FrozenSet<string>> Declared = new();

        /// <summary>The extras a class declares.</summary>
        /// <param name="producer">The service or details contributor class.</param>
        /// <returns>The names.</returns>
        public static IReadOnlySet<string> Of(Type producer)
        {
            ArgumentNullException.ThrowIfNull(producer);
            return Declared.GetOrAdd(
                producer,
                static type => type.GetCustomAttributes<DetailsExtrasAttribute>(inherit: true)
                    .SelectMany(static attribute => attribute.Names)
                    .ToFrozenSet(StringComparer.Ordinal));
        }
    }
}
