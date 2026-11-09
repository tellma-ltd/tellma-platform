// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Data.Metadata
{
    /// <summary>Source-generated log messages of the entity metadata.</summary>
    internal static partial class EntityMetadataLog
    {
        /// <summary>An entity breaks no rule but should be changed: a referenced entity has no natural key.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="entity">The entity's name.</param>
        /// <param name="detail">What is wrong and why it matters.</param>
        [LoggerMessage(EventName = "EntityMetadataWarning", Level = LogLevel.Warning, Message = "Entity {Entity}: {Detail}")]
        public static partial void Warning(ILogger logger, string entity, string detail);
    }
}
