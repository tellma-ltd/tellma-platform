// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Connector.SendGrid
{
    /// <summary>
    ///     The source-generated serializer for the SendGrid payloads: no reflection at runtime, and a
    ///     stable property order the payload snapshots can be written against.
    /// </summary>
    /// <remarks>
    ///     Omitting nulls is load-bearing rather than cosmetic — SendGrid answers 400 for several
    ///     fields sent as an explicit null.
    /// </remarks>
    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(SendGridMailRequest))]
    [JsonSerializable(typeof(SendGridErrorResponse))]
    internal sealed partial class SendGridJsonContext : JsonSerializerContext;
}
