// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     One offered language's shared resources from every composed feature, as one flat JSON
    ///     object keyed by the resource keys verbatim, emitted into the browser application's bundle
    ///     so it renders every server code and label itself.
    /// </summary>
    /// <param name="Language">The language code.</param>
    /// <param name="Version">The distribution's version, which the pack belongs to.</param>
    /// <param name="Json">The pack, pre-serialized UTF-8 JSON.</param>
    public sealed record StringPack(string Language, string Version, ReadOnlyMemory<byte> Json);
}
