// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The source-generated serialization metadata of the platform's wire records: the request
    ///     and result records, the records a problem body's details carry, and the scalar types.
    ///     <see cref="TellmaJson" /> puts it first in its resolver chain, ahead of reflection, which
    ///     resolves entity, child and operation body types.
    /// </summary>
    /// <remarks>
    ///     Metadata only, generated at build time. Use it through <see cref="TellmaJson.Options" />,
    ///     which supplies the platform's rules and converters; this context's own default options
    ///     carry none of them. A generic record is resolved by reflection, closed over its arguments
    ///     type.
    /// </remarks>
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]

    // --- Request records ---
    [JsonSerializable(typeof(QueryRequest))]
    [JsonSerializable(typeof(GetRequest))]
    [JsonSerializable(typeof(AllRequest))]
    [JsonSerializable(typeof(IdsRequest))]
    [JsonSerializable(typeof(IdRequest))]
    [JsonSerializable(typeof(GetByIdsRequest))]
    [JsonSerializable(typeof(EntityActionRequest))]
    [JsonSerializable(typeof(ParentIdsRequest))]
    [JsonSerializable(typeof(DeleteByQueryRequest))]
    [JsonSerializable(typeof(AccessHoldersRequest))]

    // --- Result records ---
    [JsonSerializable(typeof(AffectedResult))]
    [JsonSerializable(typeof(JobAccepted))]
    [JsonSerializable(typeof(SecurableSummary))]
    [JsonSerializable(typeof(QueryDiagnostic))]

    // --- The records a problem body's details carry ---
    [JsonSerializable(typeof(ConcurrencyConflict))]
    [JsonSerializable(typeof(ConfirmationRequest))]

    // --- The scalar types ---
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(byte))]
    [JsonSerializable(typeof(short))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(DateOnly))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(TimeOnly))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(byte[]))]
    public sealed partial class TellmaApiJsonContext : JsonSerializerContext;
}
