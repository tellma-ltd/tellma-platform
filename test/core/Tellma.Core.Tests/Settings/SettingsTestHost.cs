// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Settings;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Settings
{
    /// <summary>
    ///     A composition with English and Arabic offered (or the languages given) and the sample keys contributed, and a
    ///     settings store over it whose data protection is <see cref="PurposeProtection" />, or none.
    /// </summary>
    internal sealed class SettingsTestHost : IAsyncDisposable
    {
        /// <summary>The tenant every test builds settings for.</summary>
        public const int TenantId = 7;

        /// <summary>The root services.</summary>
        private readonly ServiceProvider _provider;

        /// <summary>Composes the host.</summary>
        /// <param name="withProtection">Whether the store unprotects secrets; false for a host without data protection.</param>
        /// <param name="languages">The languages the distribution offers; English and Arabic when null.</param>
        public SettingsTestHost(bool withProtection = true, string[]? languages = null)
        {
            string[] offered = languages ?? ["en", "ar"];
            _provider = TestComposition.Compose(tellma => tellma
                .Languages(offered)
                .AddFeature(SettingKeysCheckTests.Feature(typeof(SampleKeys))))
                .BuildServiceProvider(validateScopes: true);
            Protection = new PurposeProtection();
            Store = new TenantSettingsStore(
                Services.GetRequiredService<ISettingKeyRegistry>(),
                Services.GetRequiredService<ILanguageCatalog>(),
                Services.GetRequiredService<ICalendarRegistry>(),
                Options.Create(new CacheOptions()),
                Services.GetRequiredService<IMeterFactory>(),
                Log,
                withProtection ? Protection : null);
        }

        /// <summary>The root services.</summary>
        public IServiceProvider Services => _provider;

        /// <summary>The data protection secrets are protected with; the store unprotects with it unless built without.</summary>
        public PurposeProtection Protection { get; }

        /// <summary>The store's log.</summary>
        public FakeLogger<TenantSettingsStore> Log { get; } = new();

        /// <summary>The store under test.</summary>
        public TenantSettingsStore Store { get; }

        /// <summary>A row of the tenant's settings, in English with the Gregorian calendar and UTC unless changed.</summary>
        /// <param name="tenantId">The row's tenant.</param>
        /// <returns>The row.</returns>
        public static SettingsRow Row(int tenantId = TenantId)
        {
            return new SettingsRow(
                Id: 1,
                TenantId: tenantId,
                Name: "Acme",
                Name2: "أكمي",
                Name3: null,
                PrimaryLanguage: "en",
                SecondaryLanguage: null,
                TertiaryLanguage: null,
                PrimaryCalendar: "gc",
                SecondaryCalendar: null,
                TimeZone: "UTC",
                Region: null,
                Digits: null,
                CreatedAt: DateTimeOffset.UnixEpoch,
                CreatedById: 1,
                ModifiedAt: DateTimeOffset.UnixEpoch.AddDays(1),
                ModifiedById: 1);
        }

        /// <summary>Builds the tenant's settings from a row and its entries.</summary>
        /// <param name="row">The row.</param>
        /// <param name="entries">The stored entries, by key.</param>
        /// <returns>The entry.</returns>
        public TenantSettingsEntry Build(SettingsRow row, params (string Key, string Value)[] entries)
        {
            return Store.Build(TenantId, allowPlaceholder: false, VersionTag.New(), new SettingsRows(row, [.. entries.Select(static entry => KeyValuePair.Create(entry.Key, entry.Value))]));
        }

        /// <summary>A secret's stored value: the JSON string of its value's JSON, protected for the tenant.</summary>
        /// <param name="json">The value's JSON.</param>
        /// <param name="tenantId">The tenant it is protected for.</param>
        /// <returns>The stored JSON.</returns>
        public string Secret(string json, int tenantId = TenantId)
        {
            return JsonSerializer.Serialize(SettingSecrets.For(Protection, tenantId).Protect(json));
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Store.Dispose();
            return _provider.DisposeAsync();
        }

        /// <summary>The cadence an enum key holds.</summary>
        internal enum Cadence
        {
            /// <summary>Monthly.</summary>
            Monthly,

            /// <summary>Quarterly.</summary>
            Quarterly,
        }

        /// <summary>One key of each visibility, and keys of several shapes.</summary>
        internal static class SampleKeys
        {
            /// <summary>A client flag.</summary>
            public static readonly SettingKey<bool> AutoNumber = new("acme.posting.autoNumber", true, visibility: SettingVisibility.Client);

            /// <summary>A validated server width.</summary>
            public static readonly SettingKey<int> Width = new("acme.posting.width", 6, validate: static width => width is >= 1 and <= 12 ? null : "Acme.WidthOutOfRange");

            /// <summary>A client enum.</summary>
            public static readonly SettingKey<Cadence> Period = new("acme.period", Cadence.Monthly, visibility: SettingVisibility.Client);

            /// <summary>Client ids.</summary>
            public static readonly SettingKey<ImmutableArray<int>> Codes = new("acme.codes", [1, 2], visibility: SettingVisibility.Client);

            /// <summary>A secret.</summary>
            public static readonly SettingKey<string?> ApiKey = new("acme.erp.apiKey", null, visibility: SettingVisibility.Secret);
        }
    }

    /// <summary>
    ///     Data protection that tags each payload with its purpose chain, so a payload unprotects only
    ///     under the chain that protected it; anything else throws, as the real protector does.
    /// </summary>
    internal sealed class PurposeProtection : IDataProtectionProvider
    {
        /// <inheritdoc />
        public IDataProtector CreateProtector(string purpose)
        {
            return new Protector(purpose);
        }

        /// <summary>A protector of one purpose chain.</summary>
        /// <param name="chain">The chain, purposes joined by a slash.</param>
        private sealed class Protector(string chain) : IDataProtector
        {
            /// <inheritdoc />
            public IDataProtector CreateProtector(string purpose)
            {
                return new Protector(chain + "/" + purpose);
            }

            /// <inheritdoc />
            public byte[] Protect(byte[] plaintext)
            {
                return [.. Encoding.UTF8.GetBytes(chain + "|"), .. plaintext];
            }

            /// <inheritdoc />
            public byte[] Unprotect(byte[] protectedData)
            {
                byte[] prefix = Encoding.UTF8.GetBytes(chain + "|");
                return protectedData.AsSpan().StartsWith(prefix)
                    ? protectedData[prefix.Length..]
                    : throw new CryptographicException("The payload was not protected for this purpose.");
            }
        }
    }
}
