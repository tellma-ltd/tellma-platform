// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Distro.Acme.Development
{
    // Spec 0018 §6.3's end-to-end principals (spec 0024 §13). Provisioning runs in the migrator, where
    // no identity server is reachable, so instead of inviting them the sample data binds each
    // individual to the fixed subject Acme's development identity server seeds for the same address
    // (the web host's TellmaIdentity:Seed:DevUsers), as the administrator's bootstrap binds the
    // development administrator (spec 0018 §6.1).
    /// <summary>
    ///     The principals Acme's development tenants hold beside the administrator, for the client's
    ///     end-to-end suites: individuals that sign in with an email code from a fresh checkout, and a
    ///     service account.
    /// </summary>
    public static class AcmeDevelopmentPrincipals
    {
        /// <summary>The seed tenant that alone holds <see cref="SandboxOnly" />: Acme's sandbox of tenant 1.</summary>
        public const int SandboxTenantId = 2;

        /// <summary>The name of the service account every development tenant holds, a member of the <c>Sales</c> role without credentials.</summary>
        public const string ServiceAccountName = "Acme Integration";

        /// <summary>The service account's name in Arabic.</summary>
        public const string ServiceAccountName2 = "تكامل أكمي";

        /// <summary>A member of the <c>Sales</c> role, in every development tenant.</summary>
        public static readonly AcmeDevelopmentPrincipal SalesMember = new(
            "sales@localhost", "00000000-0000-0000-0000-000000000002", "Sales Representative", "مندوب المبيعات");

        /// <summary>A user whose one role grants a filtered read of the centers (the sales centers alone), in every development tenant.</summary>
        public static readonly AcmeDevelopmentPrincipal CenterReader = new(
            "centers@localhost", "00000000-0000-0000-0000-000000000003", "Center Reader", "قارئ المراكز");

        /// <summary>A member of the <c>Sales</c> role in <see cref="SandboxTenantId" /> only, a user of no other tenant.</summary>
        public static readonly AcmeDevelopmentPrincipal SandboxOnly = new(
            "sandbox@localhost", "00000000-0000-0000-0000-000000000004", "Sandbox Tester", "مختبر البيئة التجريبية");

        /// <summary>
        ///     A member of the <c>Sales</c> role in every development tenant whose pins only the English
        ///     end-to-end flows of the pinned bar write, so no other flow sees its pins change.
        /// </summary>
        public static readonly AcmeDevelopmentPrincipal PinsEnglish = new(
            "pins-en@localhost", "00000000-0000-0000-0000-000000000005", "Pins Tester", "مختبر التثبيت");

        /// <summary>
        ///     A member of the <c>Sales</c> role in every development tenant whose pins only the Arabic
        ///     end-to-end flows of the pinned bar write; those flows set its account to Arabic.
        /// </summary>
        public static readonly AcmeDevelopmentPrincipal PinsArabic = new(
            "pins-ar@localhost", "00000000-0000-0000-0000-000000000006", "Arabic Pins Tester", "مختبرة التثبيت");

        /// <summary>The individuals, in the order the development identity server seeds them.</summary>
        public static IReadOnlyList<AcmeDevelopmentPrincipal> Individuals { get; } = [SalesMember, CenterReader, SandboxOnly, PinsEnglish, PinsArabic];
    }

    /// <summary>An individual of Acme's development tenants, bound to the subject the development identity server seeds for its address.</summary>
    /// <param name="Email">The email address, which signs in with an email code.</param>
    /// <param name="Subject">The fixed identity subject.</param>
    /// <param name="Name">The name in English.</param>
    /// <param name="Name2">The name in Arabic.</param>
    public sealed record AcmeDevelopmentPrincipal(string Email, string Subject, string Name, string Name2);
}
