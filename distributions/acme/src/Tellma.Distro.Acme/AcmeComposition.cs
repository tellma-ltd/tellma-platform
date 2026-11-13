// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Composition;
using Tellma.Defaults.Azure;

namespace Tellma.Distro.Acme
{
    /// <summary>
    ///     The Acme distribution: its slug and its composition method, which the web host, the
    ///     migrator and the design-time factory all run, so each builds the same model.
    /// </summary>
    public static class AcmeComposition
    {
        /// <summary>The distribution's slug: its database names, its client id and its Data Protection application name derive from it.</summary>
        public const string Slug = "acme";

        /// <summary>
        ///     Composes the distribution: the Azure defaults and the offered languages, English (United
        ///     Kingdom) by default and Arabic (Saudi Arabia).
        /// </summary>
        /// <remarks>
        ///     Only what both hosts compose belongs here: features, entities and the services the
        ///     migrator needs too. What only the web host composes goes in its own composition.
        /// </remarks>
        /// <param name="tellma">The composition builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            ArgumentNullException.ThrowIfNull(tellma);
            _ = tellma.UseAzureDefaults().Languages(["en-GB", "ar-SA"]);
        }
    }
}
