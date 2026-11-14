// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore
{
    /// <summary>
    ///     The anonymous information document at <c>GET /api/distribution-info</c>: what a client needs
    ///     to know about the distribution before anyone signs in. No tenant list, no secret and no
    ///     instance identity.
    /// </summary>
    /// <param name="Slug">The distribution's slug.</param>
    /// <param name="DisplayName">The distribution's display name, in one language: the document is anonymous.</param>
    /// <param name="DeploymentId">The deployment's id: the slug, qualified by the environment outside Production.</param>
    /// <param name="PlatformVersion">The platform's informational version, carrying its commit.</param>
    /// <param name="DistributionVersion">The distribution's informational version, carrying its commit.</param>
    /// <param name="Identity">The identity server the distribution signs in through.</param>
    /// <param name="Surfaces">The tenant surfaces' route templates by name, null for one this instance does not serve.</param>
    /// <param name="Login">The session's sign-in route.</param>
    /// <param name="AdminVersions">The admin surface's versions this instance maps; empty when the surface is off.</param>
    /// <param name="Links">The distribution's legal links.</param>
    /// <param name="Support">Whether users may send support requests.</param>
    /// <param name="Telemetry">The SPA's own telemetry settings.</param>
    /// <param name="Session">The session's lifetime facts a client plans around.</param>
    public sealed record DistributionInfo(
        string Slug,
        string DisplayName,
        string DeploymentId,
        string PlatformVersion,
        string DistributionVersion,
        IdentityInfo Identity,
        IReadOnlyDictionary<string, string?> Surfaces,
        string Login,
        IReadOnlyList<int> AdminVersions,
        DistributionLinks Links,
        SupportInfo Support,
        ClientTelemetryInfo Telemetry,
        SessionInfo Session);

    /// <summary>The identity server of a distribution.</summary>
    /// <param name="Authority">The issuer the session signs in through and bearer tokens name.</param>
    /// <param name="Mode"><c>Standalone</c> or <c>InProc</c>.</param>
    /// <param name="AccountUrl">The identity server's account page for this distribution's client.</param>
    public sealed record IdentityInfo(string Authority, string Mode, string AccountUrl);

    /// <summary>The distribution's legal links: absolute https URLs, or null.</summary>
    /// <param name="Terms">The terms of service.</param>
    /// <param name="Privacy">The privacy policy.</param>
    public sealed record DistributionLinks(string? Terms, string? Privacy);

    /// <summary>Whether users may send support requests, and whether the provider can read them.</summary>
    /// <param name="Enabled">Whether the SPA offers support requests.</param>
    /// <param name="ProviderAccess">Whether the admin surface is mapped, through which the provider reads them.</param>
    public sealed record SupportInfo(bool Enabled, bool ProviderAccess);

    /// <summary>The SPA's own telemetry settings.</summary>
    /// <param name="Enabled">Whether the SPA relays its telemetry.</param>
    /// <param name="SampleRate">The fraction of sessions that relay it, between 0 and 1.</param>
    public sealed record ClientTelemetryInfo(bool Enabled, double SampleRate);

    /// <summary>The session's lifetime facts a client plans around.</summary>
    /// <param name="IdleDays">The session's idle lifetime in whole days, rounded up, at least 1.</param>
    public sealed record SessionInfo(int IdleDays);
}
