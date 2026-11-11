// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     The localization a flow of work renders text in, when that is not the caller's: a
    ///     notification rendered for each recipient, a welcome email for the invitee. Until the scope
    ///     returned by <see cref="Enter" /> is disposed, every localizer on this flow looks up and
    ///     formats resources in the scope's culture, and renders dates and times in its calendar and
    ///     display zone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A scope is async-local. It flows into the calls the flow makes and awaits, and into work
    ///         it starts after entering, but never into a flow that runs concurrently; disposing it
    ///         restores the scope it replaced, so scopes nest. Dispose a scope on the flow that entered
    ///         it, in reverse order of entering (a <c>using</c> declaration does both).
    ///     </para>
    ///     <para>
    ///         Outside every scope, text renders in the request's own localization: its culture is the
    ///         flow's current UI culture, and its calendar and display zone are the
    ///         <see cref="Request" /> localization the request's front door applied together with the
    ///         current cultures. Where neither was applied, dates render in the Gregorian calendar and
    ///         in UTC.
    ///     </para>
    /// </remarks>
    public static class LocalizationScope
    {
        /// <summary>The innermost scope entered on this flow, or null.</summary>
        private static readonly AsyncLocal<Frame?> Scopes = new();

        /// <summary>The request's own localization, applied on this flow, or null.</summary>
        private static readonly AsyncLocal<LocalizationContext?> RequestLocalization = new();

        /// <summary>The context of the innermost scope entered on this flow and not yet disposed; null outside every scope.</summary>
        public static LocalizationContext? Current => Scopes.Value?.Context;

        /// <summary>
        ///     The request's own localization on this flow: the context the request's front door, or the
        ///     caller of a background scope, applied with <see cref="ApplyRequest" /> alongside the
        ///     current cultures. Null where none was applied.
        /// </summary>
        public static LocalizationContext? Request => RequestLocalization.Value;

        /// <summary>Makes a context the localization of this flow until the returned scope is disposed.</summary>
        /// <param name="context">The localization of the person the text is for.</param>
        /// <returns>The scope; disposing it restores the scope it replaced. Disposing it twice does nothing more.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context" /> is null.</exception>
        public static IDisposable Enter(LocalizationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            Frame frame = new(context, Scopes.Value);
            Scopes.Value = frame;
            return frame;
        }

        /// <summary>
        ///     Applies the request's own localization to this flow: the calendar and display zone its
        ///     text renders in outside every scope, or none with null. Like the current cultures it is
        ///     applied with, it flows into what the calling method calls and awaits, never back to that
        ///     method's caller.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <c>RequestContext.ApplyCultures</c> calls this with the request context's localization,
        ///         so the method that runs the request's work applies both at once: a tenant front door
        ///         before it invokes the handler, a background caller before it runs its work.
        ///     </para>
        ///     <para>
        ///         Apply it inside the asynchronous method that runs one unit of work, so it ends with
        ///         that method. A caller that runs several units on one flow, such as a worker loop,
        ///         disposes the returned value after each unit, so a unit that applies nothing never
        ///         renders in the previous unit's calendar and zone.
        ///     </para>
        /// </remarks>
        /// <param name="context">The request's negotiated localization; null for none.</param>
        /// <returns>
        ///     A value whose disposal restores the request localization this call replaced. Disposing
        ///     it twice does nothing more.
        /// </returns>
        public static IDisposable ApplyRequest(LocalizationContext? context)
        {
            RequestApplication application = new(RequestLocalization.Value);
            RequestLocalization.Value = context;
            return application;
        }

        /// <summary>One application of a request's localization, holding the one it replaced.</summary>
        /// <param name="previous">The request localization that was applied before, or null.</param>
        private sealed class RequestApplication(LocalizationContext? previous) : IDisposable
        {
            /// <summary>Set once the application is undone, so a second disposal changes nothing.</summary>
            private int _disposed;

            /// <inheritdoc />
            public void Dispose()
            {
                // Like a scope, restored on the flow that applied it.
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    RequestLocalization.Value = previous;
                }
            }
        }

        /// <summary>One entered scope, linked to the scope it replaced.</summary>
        /// <param name="context">The scope's localization.</param>
        /// <param name="outer">The scope that was current when this one was entered, or null.</param>
        private sealed class Frame(LocalizationContext context, Frame? outer) : IDisposable
        {
            /// <summary>Set once the scope is disposed, so a second disposal changes nothing.</summary>
            private int _disposed;

            /// <summary>The scope's localization.</summary>
            public LocalizationContext Context { get; } = context;

            /// <inheritdoc />
            public void Dispose()
            {
                // Restoring is a write to the async-local on the disposing flow, which is the flow
                // that entered the scope when the scope is used as documented.
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Scopes.Value = outer;
                }
            }
        }
    }
}
