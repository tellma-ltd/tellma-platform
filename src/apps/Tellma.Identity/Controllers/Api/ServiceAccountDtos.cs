// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Identity.Controllers.Api
{
    /// <summary>The create-service-account request body.</summary>
    public sealed class CreateServiceAccountRequest
    {
        /// <summary>A human-readable name for the service account.</summary>
        [Required]
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>The API audiences the account may request.</summary>
        public IList<string> Resources { get; init; } = [];
    }

    /// <summary>The create-service-account response; the secret is returned exactly once.</summary>
    public sealed class CreateServiceAccountResponse
    {
        /// <summary>The generated client id.</summary>
        public string ClientId { get; init; } = string.Empty;

        /// <summary>The generated secret — stored only by the caller, never retrievable again.</summary>
        public string ClientSecret { get; init; } = string.Empty;
    }

    /// <summary>Service-account metadata; never includes the secret.</summary>
    public sealed class ServiceAccountResponse
    {
        /// <summary>The client id.</summary>
        public string ClientId { get; init; } = string.Empty;

        /// <summary>The human-readable name.</summary>
        public string? DisplayName { get; init; }

        /// <summary>When the account was created.</summary>
        public DateTimeOffset? CreatedUtc { get; init; }
    }
}
