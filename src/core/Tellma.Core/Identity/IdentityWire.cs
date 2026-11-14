// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Identity
{
    // The request and response bodies of the identity server's v1 management API (spec 0003 §10,
    // §11.4, with spec 0010 §A.7's existingOnly and §A.8's routes) and of its token endpoint. Each
    // shape names exactly the members the client sends or reads; a member the server adds later is
    // skipped on the way in, as §A.8 allows within a version.

    /// <summary>The bulk invite's request body.</summary>
    /// <param name="Users">The users to invite, at most a thousand.</param>
    internal sealed record InviteUsersRequest(IReadOnlyList<InviteUserItem> Users);

    /// <summary>One user of the bulk invite's request.</summary>
    /// <param name="Email">The address.</param>
    /// <param name="DisplayName">The greeting name; omitted when null.</param>
    /// <param name="Locale">The invitation's language; omitted when null.</param>
    /// <param name="Gender">The grammatical gender, as its member name; omitted when null.</param>
    /// <param name="ReturnUrl">Where the accepted invitation leads; omitted when null.</param>
    /// <param name="ExistingOnly">Whether the item is a lookup that never creates a user or sends mail; always written.</param>
    internal sealed record InviteUserItem(
        string Email,
        string? DisplayName,
        string? Locale,
        Gender? Gender,
        string? ReturnUrl,
        bool ExistingOnly);

    /// <summary>The bulk invite's response body.</summary>
    /// <param name="Results">One result per user, in request order.</param>
    internal sealed record InviteUsersResponse(IReadOnlyList<InviteUserResult>? Results);

    /// <summary>One user's result of the bulk invite.</summary>
    /// <param name="Email">The address, echoed.</param>
    /// <param name="Sub">The identity user's subject; null when the user was refused before one existed.</param>
    /// <param name="Status">The status's member name; null when the user was refused.</param>
    /// <param name="Error">The refusal; null on success.</param>
    internal sealed record InviteUserResult(string? Email, string? Sub, string? Status, string? Error);

    /// <summary>The bulk delivery status's request body.</summary>
    /// <param name="Subs">The subjects to report on, at most a thousand.</param>
    internal sealed record DeliveryStatusRequest(IReadOnlyList<string> Subs);

    /// <summary>The bulk delivery status's response body.</summary>
    /// <param name="Results">One status per subject, in request order.</param>
    internal sealed record DeliveryStatusResponse(IReadOnlyList<DeliveryStatusResult>? Results);

    /// <summary>One subject's delivery status.</summary>
    /// <param name="Sub">The subject, echoed.</param>
    /// <param name="State">The state's member name.</param>
    /// <param name="ExpectsDeliveryEvents">Whether a provider will report further.</param>
    /// <param name="SentUtc">When the message reached a transport.</param>
    /// <param name="UpdatedUtc">When a provider last reported.</param>
    /// <param name="Reason">The provider's failure detail.</param>
    internal sealed record DeliveryStatusResult(
        string? Sub,
        string? State,
        bool ExpectsDeliveryEvents,
        DateTimeOffset? SentUtc,
        DateTimeOffset? UpdatedUtc,
        string? Reason);

    /// <summary>The create-service-account request body.</summary>
    /// <param name="DisplayName">The account's display name.</param>
    /// <param name="Resources">The audiences its tokens may carry: the distribution's public origin.</param>
    internal sealed record CreateServiceAccountRequest(string DisplayName, IReadOnlyList<string> Resources);

    /// <summary>The create-service-account response body.</summary>
    /// <param name="ClientId">The new account's client id.</param>
    /// <param name="ClientSecret">Its one-time secret.</param>
    internal sealed record CreateServiceAccountResponse(string? ClientId, string? ClientSecret);

    /// <summary>The token endpoint's success body (RFC 6749 §5.1).</summary>
    /// <param name="AccessToken">The access token.</param>
    /// <param name="TokenType">The token's type; <c>Bearer</c>.</param>
    /// <param name="ExpiresIn">The token's lifetime in seconds; null when the server states none.</param>
    internal sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] long? ExpiresIn);

    /// <summary>The token endpoint's error body (RFC 6749 §5.2); only the code is read, never the description.</summary>
    /// <param name="Error">The error code, such as <c>invalid_client</c>.</param>
    internal sealed record TokenErrorResponse([property: JsonPropertyName("error")] string? Error);

    /// <summary>
    ///     The source-generated metadata of the identity wire shapes. Reached only through
    ///     <see cref="IdentityJson.Options" />, which applies the platform's JSON rules.
    /// </summary>
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(InviteUsersRequest))]
    [JsonSerializable(typeof(InviteUsersResponse))]
    [JsonSerializable(typeof(DeliveryStatusRequest))]
    [JsonSerializable(typeof(DeliveryStatusResponse))]
    [JsonSerializable(typeof(CreateServiceAccountRequest))]
    [JsonSerializable(typeof(CreateServiceAccountResponse))]
    [JsonSerializable(typeof(TokenResponse))]
    [JsonSerializable(typeof(TokenErrorResponse))]
    internal sealed partial class IdentityJsonContext : JsonSerializerContext;

    /// <summary>The JSON options of the identity wire: the platform's rules, the identity shapes' generated metadata first.</summary>
    internal static class IdentityJson
    {
        /// <summary>The read-only options.</summary>
        public static JsonSerializerOptions Options { get; } = Create();

        /// <summary>
        ///     The metadata of a wire shape under <see cref="Options" />. The context's own typed
        ///     metadata would serialize under its default options, PascalCase and with nulls, so it is
        ///     never used on its own.
        /// </summary>
        /// <typeparam name="T">The wire shape.</typeparam>
        /// <returns>The metadata.</returns>
        public static JsonTypeInfo<T> TypeInfo<T>()
        {
            return (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));
        }

        /// <summary>Creates the options and freezes them.</summary>
        /// <returns>The options.</returns>
        private static JsonSerializerOptions Create()
        {
            // camelCase out, case-insensitive in, nulls omitted, unknown members skipped, enums by
            // name: the rules the identity server's own defaults agree with.
            JsonSerializerOptions options = new();
            TellmaJson.Configure(options);
            options.TypeInfoResolverChain.Insert(0, IdentityJsonContext.Default);
            options.MakeReadOnly();
            return options;
        }
    }
}
