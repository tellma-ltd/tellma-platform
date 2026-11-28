// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     The identity of a raw statement for collapsing duplicates: its text with the holes
    ///     unfilled, its hole values (byte arrays by content, lists by reference, everything else by
    ///     equality) and its options.
    /// </summary>
    internal sealed class SqlStatementKey : IEquatable<SqlStatementKey>
    {
        /// <summary>The hole values.</summary>
        private readonly object?[] _arguments;

        /// <summary>The precomputed hash.</summary>
        private readonly int _hash;

        /// <summary>Creates the key.</summary>
        /// <param name="format">The text with its holes unfilled.</param>
        /// <param name="arguments">The hole values.</param>
        /// <param name="options">The statement's options.</param>
        public SqlStatementKey(string format, object?[] arguments, SqlOptions options)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(arguments);
            ArgumentNullException.ThrowIfNull(options);

            Format = format;
            _arguments = [.. arguments];
            Options = options;

            HashCode hash = new();
            hash.Add(format, StringComparer.Ordinal);
            hash.Add(options);
            foreach (object? argument in _arguments)
            {
                hash.Add(ArgumentHash(argument));
            }

            _hash = hash.ToHashCode();
        }

        /// <summary>The text with its holes unfilled.</summary>
        public string Format { get; }

        /// <summary>The statement's options.</summary>
        public SqlOptions Options { get; }

        /// <inheritdoc />
        public bool Equals(SqlStatementKey? other)
        {
            if (other is null || other._hash != _hash || !string.Equals(other.Format, Format, StringComparison.Ordinal)
                || !other.Options.Equals(Options) || other._arguments.Length != _arguments.Length)
            {
                return false;
            }

            for (int index = 0; index < _arguments.Length; index++)
            {
                if (!ArgumentsEqual(_arguments[index], other._arguments[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as SqlStatementKey);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return _hash;
        }

        /// <summary>Whether two hole values bind the same: scalars by value, identifiers by name, lists by reference.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they do.</returns>
        private static bool ArgumentsEqual(object? left, object? right)
        {
            return ScalarValues.IsScalar(left) || left is SqlIdentifier
                ? ScalarValues.AreEqual(left, right)
                : ReferenceEquals(left, right);
        }

        /// <summary>A hash consistent with <see cref="ArgumentsEqual" />.</summary>
        /// <param name="argument">The value.</param>
        /// <returns>The hash.</returns>
        private static int ArgumentHash(object? argument)
        {
            return ScalarValues.IsScalar(argument) || argument is SqlIdentifier
                ? ScalarValues.HashOf(argument)
                : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(argument);
        }
    }
}
