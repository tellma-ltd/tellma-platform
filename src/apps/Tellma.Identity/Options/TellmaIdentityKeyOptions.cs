// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Key material configuration. Signing and encryption use independent certificates; both
    ///     are always asymmetric X.509 by construction, so access tokens can be validated offline
    ///     against the published JWKS.
    /// </summary>
    public sealed class TellmaIdentityKeyOptions
    {
        /// <summary>The token-signing certificate source (public half published in JWKS).</summary>
        public TellmaIdentityCertificateOptions Signing { get; } = new TellmaIdentityCertificateOptions();

        /// <summary>
        ///     The token-encryption certificate source (protects refresh tokens, authorization
        ///     codes, and device codes; never published).
        /// </summary>
        public TellmaIdentityCertificateOptions Encryption { get; } = new TellmaIdentityCertificateOptions();
    }

    /// <summary>Where certificates are loaded from.</summary>
    public enum TellmaIdentityCertificateSourceKind
    {
        /// <summary>Not configured; startup validation fails until a source is chosen.</summary>
        None = 0,

        /// <summary>
        ///     Self-signed certificates generated on first run and persisted under the local
        ///     application-data folder. Development only; rejected outside it.
        /// </summary>
        DevelopmentSelfSigned = 1,

        /// <summary>PKCS#12 (PFX) files on disk — the on-prem path.</summary>
        PfxFile = 2,

        /// <summary>
        ///     The current user's certificate store, looked up by thumbprint — the Azure App
        ///     Service <c>WEBSITE_LOAD_CERTIFICATES</c> path, also usable on-prem.
        /// </summary>
        CertificateStore = 3,

        /// <summary>
        ///     Azure Key Vault certificates via managed identity. All enabled, non-expired
        ///     versions are loaded so overlap rotation works.
        /// </summary>
        KeyVault = 4,
    }

    /// <summary>One certificate slot (signing or encryption) and its source configuration.</summary>
    public sealed class TellmaIdentityCertificateOptions
    {
        /// <summary>The kind of source certificates are loaded from.</summary>
        public TellmaIdentityCertificateSourceKind Source { get; set; }

        /// <summary>PFX files, used when <see cref="Source" /> is <see cref="TellmaIdentityCertificateSourceKind.PfxFile" />.</summary>
        public IList<TellmaIdentityPfxFileOptions> PfxFiles { get; } = [];

        /// <summary>Store thumbprints, used when <see cref="Source" /> is <see cref="TellmaIdentityCertificateSourceKind.CertificateStore" />.</summary>
        public IList<string> StoreThumbprints { get; } = [];

        /// <summary>Key Vault settings, used when <see cref="Source" /> is <see cref="TellmaIdentityCertificateSourceKind.KeyVault" />.</summary>
        public TellmaIdentityKeyVaultCertificateOptions KeyVault { get; } = new TellmaIdentityKeyVaultCertificateOptions();
    }

    /// <summary>A single PFX file reference.</summary>
    public sealed class TellmaIdentityPfxFileOptions
    {
        /// <summary>Absolute path of the PKCS#12 file.</summary>
        public string? Path { get; set; }

        /// <summary>The file password, when the file is protected.</summary>
        public string? Password { get; set; }
    }

    /// <summary>Azure Key Vault certificate lookup settings.</summary>
    public sealed class TellmaIdentityKeyVaultCertificateOptions
    {
        /// <summary>The vault URI, for example <c>https://my-vault.vault.azure.net/</c>.</summary>
        public Uri? VaultUri { get; set; }

        /// <summary>The certificate name inside the vault.</summary>
        public string? CertificateName { get; set; }

        /// <summary>
        ///     When true (the default), every enabled, non-expired version of the certificate is
        ///     loaded — the newest signs, older ones stay published for validation — which is what
        ///     makes overlap rotation a pure Key Vault operation.
        /// </summary>
        public bool IncludeAllEnabledVersions { get; set; } = true;
    }
}
