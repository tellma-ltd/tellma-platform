// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     The base of the platform's closed set of exceptions: the only exceptions a service, the
    ///     pipeline or a platform feature throws for an outcome a caller can act on. The web edge and
    ///     the agent tools map each member by its exact type to a problem status and code.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The set is closed by construction: only this assembly can derive from this class, and
    ///         every member is sealed, so no exception reaches a mapping through a base type, and a
    ///         type the edge does not know is a 500 with a trace id. An authoring bug (an unknown
    ///         action, a validator exceeding its rounds) throws a BCL exception instead, because it
    ///         is not a user outcome.
    ///     </para>
    ///     <para>
    ///         <see cref="Code" /> and <see cref="Arguments" /> are the contract a client renders
    ///         from; the exception message is English text for logs only and never reaches a
    ///         response.
    ///     </para>
    /// </remarks>
    public abstract class TellmaException : Exception
    {
        /// <summary>Creates a member of the closed set.</summary>
        /// <param name="code">The outcome's stable, machine-readable code.</param>
        /// <param name="message">English text describing the outcome, for logs.</param>
        /// <param name="arguments">The raw display values of the outcome; none when null.</param>
        /// <param name="innerException">The exception that caused this one, if any.</param>
        /// <exception cref="ArgumentException"><paramref name="code" /> or <paramref name="message" /> is null, empty or white space.</exception>
        private protected TellmaException(
            string code, string message, IReadOnlyDictionary<string, object?>? arguments = null, Exception? innerException = null)
            : base(ValidateMessage(message), innerException)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            Code = code;
            Arguments = arguments is null ? ReadOnlyDictionary<string, object?>.Empty : ReadOnlyCopies.Of(arguments);
        }

        /// <summary>
        ///     The outcome's stable, machine-readable code. For most members it is the kebab-case
        ///     problem code the response carries (<c>not-found</c>, <c>forbidden</c>); a member whose
        ///     status depends on a finer code documents what its code holds. Codes are never renamed;
        ///     a platform minor may add one.
        /// </summary>
        public string Code { get; }

        /// <summary>
        ///     The raw display values of the outcome, keyed by name: strings, numbers, booleans,
        ///     dates and lists of them, never pre-formatted text. A client renders a message from
        ///     <see cref="Code" /> and these values in its own language, and the edge copies them into
        ///     the problem body's <c>arguments</c>. Empty when the outcome carries none.
        /// </summary>
        public IReadOnlyDictionary<string, object?> Arguments { get; }

        /// <summary>Refuses an empty log message before the base constructor stores it.</summary>
        /// <param name="message">The message.</param>
        /// <returns>The message.</returns>
        private static string ValidateMessage(string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            return message;
        }
    }
}
