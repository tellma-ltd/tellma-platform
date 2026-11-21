// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Observability;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     Tells the session-termination listeners that sessions ended: each listener in registration
    ///     order, in a scope of their own, a failing listener logged and never thrown, so a later
    ///     listener is still told and the sign-out or revocation it follows still completes.
    /// </summary>
    /// <param name="scopes">Creates the listeners' scope.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class SessionTerminationNotifier(IServiceScopeFactory scopes, ILogger<SessionTerminationNotifier> logger)
    {
        /// <summary>Tells every listener which sessions of which subjects ended, one call per subject.</summary>
        /// <param name="ended">The ended sessions.</param>
        /// <returns>A task that completes when every listener was told or its failure logged.</returns>
        public async Task NotifyAsync(IReadOnlyList<EndedSession> ended)
        {
            ArgumentNullException.ThrowIfNull(ended);
            foreach (IGrouping<string, EndedSession> subject in ended.GroupBy(static session => session.Subject, StringComparer.Ordinal))
            {
                await NotifyAsync(subject.Key, [.. subject.Select(static session => session.Handle)]).ConfigureAwait(false);
            }
        }

        /// <summary>Tells every listener that sessions of one subject ended.</summary>
        /// <param name="subject">The subject.</param>
        /// <param name="handles">The sessions' handles; never their keys.</param>
        /// <returns>A task that completes when every listener was told or its failure logged.</returns>
        public async Task NotifyAsync(string subject, IReadOnlyList<string> handles)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(handles);
            if (handles.Count == 0)
            {
                return;
            }

            AsyncServiceScope scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                IEnumerable<ISessionTerminationListener> listeners;
                try
                {
                    listeners = [.. scope.ServiceProvider.GetServices<ISessionTerminationListener>()];
                }
                catch (Exception exception)
                {
                    HostLog.SessionListenerFailed(logger, nameof(ISessionTerminationListener), exception);
                    return;
                }

                foreach (ISessionTerminationListener listener in listeners)
                {
                    try
                    {
                        // A listener that hands back no task fails like one that throws.
                        await (listener.SessionsTerminatedAsync(subject, handles)
                            ?? throw new InvalidOperationException("The session-termination listener returned no task.")).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        HostLog.SessionListenerFailed(logger, listener.GetType().FullName ?? listener.GetType().Name, exception);
                    }
                }
            }
        }
    }
}
