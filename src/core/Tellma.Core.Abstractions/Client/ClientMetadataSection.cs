// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>One member of a client metadata chunk, as a contributor wrote it.</summary>
    public sealed class ClientMetadataSection
    {
        /// <summary>Creates a section; only <see cref="ClientAssetContext" /> does, after validating it.</summary>
        /// <param name="chunk">The chunk the member belongs to.</param>
        /// <param name="name">The member's name.</param>
        /// <param name="json">The member's value as UTF-8 JSON.</param>
        internal ClientMetadataSection(ClientMetadataChunk chunk, string name, ReadOnlyMemory<byte> json)
        {
            Chunk = chunk;
            Name = name;
            Json = json;
        }

        /// <summary>The chunk the member belongs to.</summary>
        public ClientMetadataChunk Chunk { get; }

        /// <summary>The member's camelCase name.</summary>
        public string Name { get; }

        /// <summary>The member's value: exactly one complete JSON value, as UTF-8.</summary>
        public ReadOnlyMemory<byte> Json { get; }
    }
}
