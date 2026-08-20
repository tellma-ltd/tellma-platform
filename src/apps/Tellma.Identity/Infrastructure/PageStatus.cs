// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Infrastructure
{
    /// <summary>What a status message is telling the user, which decides how it is presented.</summary>
    public enum PageStatusKind
    {
        /// <summary>Something the user asked for happened.</summary>
        Success,

        /// <summary>Context the user did not ask for and need not act on.</summary>
        Info,

        /// <summary>Something to weigh before continuing.</summary>
        Warning,

        /// <summary>An action was refused.</summary>
        Error,
    }

    /// <summary>
    ///     A banner above a page's content.
    ///     <para>
    ///         The kind is carried rather than inferred, because one grey paragraph cannot serve
    ///         both "your changes have been saved" and "you cannot remove your only sign-in
    ///         method": telling them apart by wording alone leaves the distinction invisible to
    ///         anyone who cannot read the difference, and a refused action that is not announced
    ///         as a refusal looks like nothing happened.
    ///     </para>
    /// </summary>
    /// <param name="Kind">How to present it.</param>
    /// <param name="Text">The message.</param>
    public sealed record PageStatus(PageStatusKind Kind, string Text)
    {
        /// <summary>A message about something that succeeded.</summary>
        /// <param name="text">The message.</param>
        /// <returns>The status.</returns>
        public static PageStatus Success(string text)
        {
            return new PageStatus(PageStatusKind.Success, text);
        }

        /// <summary>A message that carries context rather than an outcome.</summary>
        /// <param name="text">The message.</param>
        /// <returns>The status.</returns>
        public static PageStatus Info(string text)
        {
            return new PageStatus(PageStatusKind.Info, text);
        }

        /// <summary>A message about something to weigh before continuing.</summary>
        /// <param name="text">The message.</param>
        /// <returns>The status.</returns>
        public static PageStatus Warning(string text)
        {
            return new PageStatus(PageStatusKind.Warning, text);
        }

        /// <summary>A message about an action that was refused.</summary>
        /// <param name="text">The message.</param>
        /// <returns>The status.</returns>
        public static PageStatus Error(string text)
        {
            return new PageStatus(PageStatusKind.Error, text);
        }
    }
}
