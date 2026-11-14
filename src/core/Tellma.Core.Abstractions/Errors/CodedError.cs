// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     What every coded error item shares: a code and the raw arguments its message renders from.
    ///     A validation error adds the path of the value it concerns; other features add their own
    ///     coordinates (an import error's sheet and row, a job's failure).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A code is a dotted PascalCase resource key: the platform's are the constants of
    ///         <see cref="Validation.ValidationCodes" /> and the Core features' families
    ///         (<c>Users.NotService</c>); a pack or distribution names its own
    ///         <c>&lt;Schema&gt;.&lt;Area&gt;.&lt;Rule&gt;</c>, schema first
    ///         (<c>Gl.Centers.ParentMustBeGrouping</c>), so two features never declare one resource
    ///         key. A platform minor may add codes, never rename one.
    ///     </para>
    ///     <para>
    ///         Arguments are named raw values (strings, numbers, booleans, <see cref="DateOnly" /> and
    ///         <see cref="DateTimeOffset" />), never pre-formatted text: the server renders the
    ///         message under the request culture and the client renders it again from its string
    ///         pack in the language the user is viewing. A property is named by its CLR name under
    ///         <c>property</c>, which each renderer resolves to a label itself.
    ///     </para>
    ///     <para>
    ///         Both members are get-only and copied on construction, so neither a <c>with</c>
    ///         expression nor the caller's original dictionary can change an error once created. Two
    ///         errors are equal when they are of the same type and their codes, their arguments
    ///         (compared name by name, each value by <see cref="object.Equals(object, object)" />) and
    ///         the members a derived type adds are equal.
    ///     </para>
    /// </remarks>
    /// <param name="Code">The error's code, a dotted PascalCase resource key.</param>
    /// <param name="Arguments">The raw values its message renders from; empty when it has none.</param>
    public abstract record CodedError(string Code, IReadOnlyDictionary<string, object?> Arguments)
    {
        /// <inheritdoc cref="CodedError(string, IReadOnlyDictionary{string, object?})" />
        public string Code { get; } = ValidateCode(Code);

        /// <inheritdoc cref="CodedError(string, IReadOnlyDictionary{string, object?})" />
        public IReadOnlyDictionary<string, object?> Arguments { get; } = CopyArguments(Arguments);

        /// <summary>
        ///     Whether another error is of the same type and carries an equal code and equal arguments.
        ///     A derived type compares its own members on top.
        /// </summary>
        /// <param name="other">The other error.</param>
        /// <returns>True when the two are equal.</returns>
        public virtual bool Equals(CodedError? other)
        {
            // The argument bag is compared by content: each error holds its own copy, so the
            // reference comparison a record would synthesise could never find two errors equal.
            return other is not null
                && EqualityContract == other.EqualityContract
                && string.Equals(Code, other.Code, StringComparison.Ordinal)
                && ArgumentBags.Equal(Arguments, other.Arguments);
        }

        /// <summary>A hash consistent with <see cref="Equals(CodedError)" />.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(EqualityContract, StringComparer.Ordinal.GetHashCode(Code), Arguments.Count);
        }

        /// <summary>Refuses an empty code.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The code.</returns>
        private static string ValidateCode(string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            return code;
        }

        /// <summary>Refuses a null argument bag and copies the rest.</summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A read-only copy.</returns>
        private static IReadOnlyDictionary<string, object?> CopyArguments(IReadOnlyDictionary<string, object?> arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            return ReadOnlyCopies.Of(arguments);
        }
    }
}
