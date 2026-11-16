// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Crud.Validation
{
    /// <summary>
    ///     The confirmation keys the composition's features declared, which are the only keys a
    ///     warning may be raised or confirmed under.
    /// </summary>
    /// <param name="keys">The declared keys.</param>
    internal sealed class DeclaredConfirmations(IEnumerable<string> keys)
    {
        /// <summary>The declared keys, compared ordinally: a key is a resource key and is spelled one way.</summary>
        public FrozenSet<string> Keys { get; } = (keys ?? throw new ArgumentNullException(nameof(keys))).ToFrozenSet(StringComparer.Ordinal);

        /// <summary>Refuses a key no feature declared.</summary>
        /// <param name="key">The key.</param>
        /// <param name="parameterName">The caller's parameter.</param>
        /// <exception cref="InvalidOperationException">The key was not declared.</exception>
        public void EnsureDeclared(string key, string parameterName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, parameterName);
            if (!Keys.Contains(key))
            {
                throw new InvalidOperationException(
                    $"The confirmation '{key}' was never declared. Declare each confirmation once in the composition of the feature that raises it, with contribution.Confirmation(\"{key}\").");
            }
        }
    }
}
