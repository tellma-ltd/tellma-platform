// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Localization
{
    // The packs are spec 0013 §10.4's, delivered as spec 0024 §3.2's emit names them.
    /// <summary>
    ///     Writes the composition's string packs into the client bundle: one per offered language,
    ///     and English, the fallback of every other pack.
    /// </summary>
    /// <param name="composer">The composer of the packs.</param>
    internal sealed class StringPackClientAssetContributor(StringPackComposer composer) : IClientAssetContributor
    {
        /// <inheritdoc />
        public void Contribute(ClientAssetContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            foreach (StringPack pack in composer.Packs)
            {
                context.StringPack(pack);
            }
        }
    }
}
