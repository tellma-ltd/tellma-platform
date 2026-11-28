// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Composition;
using Tellma.Defaults.Azure;
using Tellma.Distro.Acme.Entities;
using Tellma.Module.Gl;

namespace Tellma.Distro.Acme
{
    // Spec 0018 §1.2: the reference distribution's shared composition, invoked by both hosts as
    // spec 0011 §1.3 shows; the default regions of the two languages are spec 0013 §7.3's.
    /// <summary>
    ///     The Acme distribution: its slug and its composition method, which the web host, the
    ///     migrator and the design-time factory all run, so each builds the same model.
    /// </summary>
    public static class AcmeComposition
    {
        /// <summary>The distribution's slug: its database names, its client id and its Data Protection application name derive from it.</summary>
        public const string Slug = "acme";

        /// <summary>
        ///     Composes the distribution: the Azure defaults, the general ledger with Acme's center
        ///     leaf <see cref="MyCenter" /> in place of its center, Acme's own feature, and the offered
        ///     languages, English (United Kingdom) by default and Arabic (Saudi Arabia).
        /// </summary>
        /// <remarks>
        ///     Only what both hosts compose belongs here: features, entities and the services the
        ///     migrator needs too. What only the web host composes goes in its own composition.
        /// </remarks>
        /// <param name="tellma">The composition builder.</param>
        /// <exception cref="ArgumentNullException"><paramref name="tellma" /> is null.</exception>
        public static void Compose(TellmaBuilder tellma)
        {
            ArgumentNullException.ThrowIfNull(tellma);
            _ = tellma.UseAzureDefaults()
                .AddFeature<GlFeature>()
                .AddFeature<AcmeFeature>()
                .UseEntity<Center, MyCenter>()
                .Languages(["en-GB", "ar-SA"]);
        }
    }
}
