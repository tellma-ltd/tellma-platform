// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     Reads host options straight from configuration, unvalidated, for the decisions the host makes
    ///     while it is built: what <c>UseTellma</c> installs and what <c>MapTellma</c> maps. Validation
    ///     is the startup gate's, which reports every problem at once; resolving the validated options
    ///     here would fail on the first.
    /// </summary>
    internal static class HostSettings
    {
        /// <summary>Binds a section, or answers the defaults when it is absent or cannot be bound.</summary>
        /// <typeparam name="TOptions">The options type.</typeparam>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="sectionPath">The section's path.</param>
        /// <returns>The options.</returns>
        public static TOptions Read<TOptions>(IConfiguration configuration, string sectionPath)
            where TOptions : class, new()
        {
            ArgumentNullException.ThrowIfNull(configuration);
            try
            {
                return configuration.GetSection(sectionPath).Get<TOptions>() ?? new TOptions();
            }
            catch (InvalidOperationException)
            {
                // A value the binder cannot convert; the gate's options check names it.
                return new TOptions();
            }
        }
    }
}
