// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Composition;
using Tellma.Core.Email;

namespace Tellma.Distro.Acme.Web.Development
{
    // The capturing sender of spec 0024 §13's e2e host, out of process: the browser suites drive the
    // web host through `dotnet run`, so they cannot reach an in-process sender (spec 0003 §10.6), and
    // read the sign-in codes from the files this transport drops instead.
    /// <summary>
    ///     The development mail drop: an email transport that writes every message to a directory as
    ///     one JSON file, for the browser suites that drive this host from another process and read
    ///     the sign-in codes the identity engine mails. It runs only in Development, where the suites
    ///     select it with <c>Email:Provider</c> set to <see cref="TransportName" /> and
    ///     <see cref="DirectoryKey" /> set to an absolute directory.
    /// </summary>
    /// <remarks>
    ///     Like the platform's log sink, the drop keeps every message, codes and links included, and
    ///     delivers none; outside Development it would swallow real mail and write it to disk. A
    ///     startup check therefore refuses either setting outside Development, and the sender itself
    ///     refuses to be created there.
    /// </remarks>
    public static class AcmeMailDrop
    {
        /// <summary>The transport's configuration name, which <c>Email:Provider</c> selects.</summary>
        public const string TransportName = "development-drop";

        /// <summary>The configuration section of the drop.</summary>
        public const string SectionName = "Email:DevelopmentDrop";

        /// <summary>The configuration key of the directory the drop writes to.</summary>
        public const string DirectoryKey = SectionName + ":Directory";

        /// <summary>The configuration key of the active email transport.</summary>
        internal const string ProviderKey = "Email:Provider";

        /// <summary>The name of the startup check in diagnostics.</summary>
        internal const string CheckName = "acme.mail-drop";

        /// <summary>
        ///     Registers the transport and its startup check. The transport is registered in every
        ///     environment, so that a deployment selecting it outside Development is told why it is
        ///     refused rather than that no such transport exists.
        /// </summary>
        /// <param name="tellma">The composition builder.</param>
        public static void Register(TellmaBuilder tellma)
        {
            ArgumentNullException.ThrowIfNull(tellma);

            // --- The transport: the same drop for the live and the sandbox channel ---
            // A sandbox tenant's external mail is worth reading in development too, as the log sink's is.
            _ = tellma.Services.AddSingleton(new EmailTransportRegistration(
                TransportName,
                static services => Create(services, EmailChannel.Live),
                static services => Create(services, EmailChannel.Sandbox)));

            // --- The startup check: Development only, an absolute directory, and both settings together ---
            _ = tellma.Services.AddSingleton<IStartupCheck>(new AcmeMailDropCheck());
        }

        /// <summary>Creates the sender of one channel over the configured directory.</summary>
        /// <param name="services">The services.</param>
        /// <param name="channel">The channel.</param>
        /// <returns>The sender.</returns>
        private static AcmeMailDropSender Create(IServiceProvider services, EmailChannel channel)
        {
            IConfiguration configuration = services.GetRequiredService<IConfiguration>();
            return new AcmeMailDropSender(
                services.GetRequiredService<IHostEnvironment>(),
                configuration[DirectoryKey],
                channel,
                services.GetService<TimeProvider>() ?? TimeProvider.System);
        }
    }
}
