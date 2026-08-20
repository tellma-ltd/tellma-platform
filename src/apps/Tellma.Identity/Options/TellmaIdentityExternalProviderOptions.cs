// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     External login providers. A provider is registered only when its client id is
    ///     configured, and offered at sign-in only when the tenant's allow-list permits it.
    /// </summary>
    public sealed class TellmaIdentityExternalProviderOptions
    {
        /// <summary>Google sign-in configuration.</summary>
        public TellmaIdentityExternalProviderCredentials Google { get; } = new TellmaIdentityExternalProviderCredentials();

        /// <summary>Microsoft-account sign-in configuration.</summary>
        public TellmaIdentityExternalProviderCredentials Microsoft { get; } = new TellmaIdentityExternalProviderCredentials();
    }

    /// <summary>OAuth client credentials for one external provider.</summary>
    public sealed class TellmaIdentityExternalProviderCredentials
    {
        /// <summary>The OAuth client id; the provider is registered only when this is set.</summary>
        public string? ClientId { get; set; }

        /// <summary>The OAuth client secret.</summary>
        public string? ClientSecret { get; set; }

        /// <summary>Whether the provider is configured and should be registered.</summary>
        internal bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
    }
}
