// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>The hashed JSON chunk of the client metadata a section belongs to.</summary>
    public enum ClientMetadataChunk
    {
        /// <summary>The core chunk, loaded before the client's first render on every route.</summary>
        Core,

        /// <summary>The entities chunk, loaded on entity routes only.</summary>
        Entities,

        /// <summary>The contracts chunk, loaded once before the client's first API call.</summary>
        Contracts,
    }
}
