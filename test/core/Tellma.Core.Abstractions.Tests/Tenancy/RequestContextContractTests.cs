// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Tests.Tenancy
{
    /// <summary>
    ///     The request-context contract: the record's immutability, defaults and member rules, the
    ///     tenantless context, the snapshot's round trip, the assurance bar and its exception, the
    ///     tenant scope's disposal, and the collection services.
    /// </summary>
    public class RequestContextContractTests
    {
        private static readonly DateTimeOffset Start = new(2026, 11, 9, 22, 30, 0, TimeSpan.FromHours(3));

        private static readonly TenantDescriptor Live = new(17, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null);

        private static readonly TenantDescriptor Sandbox = new(18, "Acme Sandbox", null, null, ["en"], TenantCategory.Sandbox, TenantState.Active, 17);

        private static RequestContext Tenantless => RequestContext.Tenantless(new StubCalendarSystem("gc"), Start);

        [Fact]
        public void The_tenantless_context_has_no_tenant_an_anonymous_principal_and_the_platform_defaults()
        {
            StubCalendarSystem gregorian = new("gc");

            var context = RequestContext.Tenantless(gregorian, Start);

            Assert.Null(context.Tenant);
            Assert.Equal(PrincipalKind.Anonymous, context.Kind);
            Assert.Null(context.Subject);
            Assert.Null(context.ClientId);
            Assert.Null(context.SessionId);
            Assert.Null(context.SessionHandle);
            Assert.Null(context.Assurance);
            Assert.Null(context.UserId);
            Assert.Null(context.TenantSettings);
            Assert.Null(context.OriginTraceParent);
            Assert.Equal(("en", "en", "gc", 1, "UTC", "UTC", "web"), (context.Language, context.Culture, context.Calendar, context.ContentLanguageIndex, context.TimeZone, context.TenantTimeZone, context.Client));
            Assert.Same(gregorian, context.CalendarSystem);
            Assert.Equal("en", context.CultureInfo.Name);
            Assert.Equal(Start, context.Now);
            Assert.False(context.IsSandbox || context.IsSystem || context.IsServiceAccount);
            _ = Assert.Throws<ArgumentNullException>(() => RequestContext.Tenantless(null!, Start));
        }

        [Fact]
        public void The_tenantless_today_is_the_date_of_now_in_utc()
        {
            // 22:30 at +03:00 is 19:30 UTC the same day; 01:30 at +03:00 is still the day before in UTC.
            Assert.Equal(new DateOnly(2026, 11, 9), Tenantless.Today);
            Assert.Equal(
                new DateOnly(2026, 11, 9),
                RequestContext.Tenantless(new StubCalendarSystem("gc"), new DateTimeOffset(2026, 11, 10, 1, 30, 0, TimeSpan.FromHours(3))).Today);
        }

        [Fact]
        public void No_member_can_be_set_after_the_context_is_built()
        {
            PropertyInfo[] properties = typeof(RequestContext).GetProperties(BindingFlags.Instance | BindingFlags.Public);

            Assert.NotEmpty(properties);
            Assert.All(properties, static property =>
            {
                // A member is read-only or init-only: an init accessor carries the IsExternalInit modifier.
                MethodInfo? setter = property.SetMethod;
                Assert.True(
                    setter is null || setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)),
                    property.Name);
            });
            Assert.True(typeof(RequestContext).IsSealed);
            Assert.DoesNotContain(
                typeof(RequestContext).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                static field => !field.IsInitOnly);
        }

        [Fact]
        public void A_copy_leaves_the_context_it_was_made_from_unchanged()
        {
            RequestContext original = Tenantless;

            RequestContext copy = original with { Tenant = Live, Kind = PrincipalKind.User, Subject = "sub-1", UserId = 5, Language = "ar" };

            Assert.Null(original.Tenant);
            Assert.Equal((PrincipalKind.Anonymous, null, null, "en"), (original.Kind, original.Subject, original.UserId, original.Language));
            Assert.Equal(((TenantDescriptor?)Live, PrincipalKind.User, (string?)"sub-1", 5, "ar"), (copy.Tenant, copy.Kind, copy.Subject, copy.UserId, copy.Language));
            Assert.NotEqual(original, copy);
            Assert.Equal(copy, copy with { });
        }

        [Fact]
        public void The_culture_object_is_stored_read_only()
        {
            // A fresh, writable culture goes in; what comes out cannot be changed by any reader.
            CultureInfo writable = new("ar-SA");
            Assert.False(writable.IsReadOnly);

            RequestContext context = Tenantless with { Culture = "ar-SA", CultureInfo = writable };

            Assert.True(context.CultureInfo.IsReadOnly);
            Assert.Equal("ar-SA", context.CultureInfo.Name);
            Assert.True(Tenantless.CultureInfo.IsReadOnly);
            _ = Assert.Throws<InvalidOperationException>(() => context.CultureInfo.NumberFormat.NumberDecimalSeparator = "x");
        }

        [Fact]
        public void Applying_a_contexts_cultures_sets_the_current_culture_and_the_current_ui_culture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            CultureInfo originalUi = CultureInfo.CurrentUICulture;
            RequestContext context = Tenantless with { Culture = "ar-SA", CultureInfo = new CultureInfo("ar-SA") };

            try
            {
                context.ApplyCultures();

                // The very object the context holds, so its number and date formats are the negotiated ones.
                Assert.Same(context.CultureInfo, CultureInfo.CurrentCulture);
                Assert.Same(context.CultureInfo, CultureInfo.CurrentUICulture);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
                CultureInfo.CurrentUICulture = originalUi;
            }
        }

        [Fact]
        public void The_calendar_code_is_the_calendar_systems()
        {
            RequestContext context = Tenantless with { CalendarSystem = new StubCalendarSystem("uq") };

            Assert.Equal("uq", context.Calendar);
            Assert.Null(typeof(RequestContext).GetProperty(nameof(RequestContext.Calendar))!.SetMethod);
        }

        [Fact]
        public void A_member_refuses_a_value_that_breaks_its_rule()
        {
            RequestContext context = Tenantless;

            _ = Assert.Throws<ArgumentException>(() => context with { Language = " " });
            _ = Assert.Throws<ArgumentException>(() => context with { Culture = "" });
            _ = Assert.Throws<ArgumentException>(() => context with { TimeZone = null! });
            _ = Assert.Throws<ArgumentException>(() => context with { TenantTimeZone = "" });
            _ = Assert.Throws<ArgumentException>(() => context with { Client = " " });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => context with { ContentLanguageIndex = 0 });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => context with { ContentLanguageIndex = 4 });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => context with { Kind = (PrincipalKind)9 });
            _ = Assert.Throws<ArgumentNullException>(() => context with { CalendarSystem = null! });
            _ = Assert.Throws<ArgumentNullException>(() => context with { CultureInfo = null! });
            Assert.Equal(3, (context with { ContentLanguageIndex = 3 }).ContentLanguageIndex);
        }

        [Fact]
        public void The_flags_derive_from_the_tenant_and_the_kind()
        {
            RequestContext context = Tenantless;

            Assert.True((context with { Tenant = Sandbox }).IsSandbox);
            Assert.False((context with { Tenant = Live }).IsSandbox);
            Assert.True((context with { Kind = PrincipalKind.System }).IsSystem);
            Assert.False((context with { Kind = PrincipalKind.System }).IsServiceAccount);
            Assert.True((context with { Kind = PrincipalKind.ServiceAccount }).IsServiceAccount);
            Assert.False((context with { Kind = PrincipalKind.User }).IsSystem);
        }

        [Fact]
        public void Requiring_a_tenant_answers_the_bound_one_and_refuses_tenantless_work()
        {
            Assert.Same(Live, (Tenantless with { Tenant = Live }).RequireTenant());

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(Tenantless.RequireTenant);
            Assert.Contains("not bound to a tenant", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_stored_names_of_principal_kinds_are_pinned()
        {
            // A background caller stores the name with the work it enqueues: renaming one strands that work.
            Assert.Equal(["Anonymous", "User", "ServiceAccount", "System"], Enum.GetNames<PrincipalKind>());
            Assert.Equal(PrincipalKind.Anonymous, default);
        }

        [Fact]
        public void A_snapshot_round_trips_through_json()
        {
            RequestContextSnapshot snapshot = new(17, PrincipalKind.User, "sub-1", 5, "worker", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

            string json = JsonSerializer.Serialize(snapshot);
            RequestContextSnapshot? read = JsonSerializer.Deserialize<RequestContextSnapshot>(json);

            Assert.Equal(snapshot, read);
            Assert.Equal(
                (17, PrincipalKind.User, (string?)"sub-1", 5, "worker", (string?)"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"),
                (read!.TenantId, read.Kind, read.Subject, read.UserId, read.Client, read.TraceParent));

            // A system snapshot carries neither a user nor a trace parent.
            RequestContextSnapshot system = new(17, PrincipalKind.System, "system", null, "worker", null);
            Assert.Equal(system, JsonSerializer.Deserialize<RequestContextSnapshot>(JsonSerializer.Serialize(system)));
        }

        [Fact]
        public void A_snapshot_carries_no_locale()
        {
            Assert.Equal(
                ["Client", "Kind", "Subject", "TenantId", "TraceParent", "UserId"],
                typeof(RequestContextSnapshot).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(static property => property.Name).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void A_snapshot_refuses_a_tenant_id_a_kind_or_a_client_that_names_nothing()
        {
            RequestContextSnapshot snapshot = new(17, PrincipalKind.User, "sub-1", 5, "worker", null);

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RequestContextSnapshot(0, PrincipalKind.User, "sub-1", 5, "worker", null));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RequestContextSnapshot(17, (PrincipalKind)9, "sub-1", 5, "worker", null));
            _ = Assert.Throws<ArgumentException>(() => new RequestContextSnapshot(17, PrincipalKind.User, "sub-1", 5, " ", null));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => snapshot with { TenantId = -1 });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => snapshot with { Kind = (PrincipalKind)9 });
            _ = Assert.Throws<ArgumentException>(() => snapshot with { Client = "" });
            _ = Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<RequestContextSnapshot>("""{"TenantId":0,"Kind":1,"Subject":"s","UserId":5,"Client":"worker","TraceParent":null}"""));
        }

        [Fact]
        public void The_empty_inputs_ask_for_nothing()
        {
            RequestContextInputs empty = RequestContextInputs.Empty;

            Assert.Equal(new RequestContextInputs(null, null, null, null, null, null), empty);
            Assert.Same(empty, RequestContextInputs.Empty);
        }

        [Fact]
        public void An_assurance_bar_names_a_class_and_a_positive_age_or_none()
        {
            RequireAssuranceMetadata bar = new("mfa", TimeSpan.FromMinutes(10));

            Assert.Equal(("mfa", (TimeSpan?)TimeSpan.FromMinutes(10)), (bar.Acr, bar.MaxAge));
            Assert.Null(new RequireAssuranceMetadata("mfa", null).MaxAge);
            Assert.Equal(bar, new RequireAssuranceMetadata("mfa", TimeSpan.FromMinutes(10)));
            _ = Assert.Throws<ArgumentException>(() => new RequireAssuranceMetadata(" ", null));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RequireAssuranceMetadata("mfa", TimeSpan.Zero));
            _ = Assert.Throws<ArgumentException>(() => bar with { Acr = "" });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => bar with { MaxAge = TimeSpan.FromSeconds(-1) });
        }

        [Fact]
        public void A_step_up_names_the_bar_and_carries_no_display_arguments()
        {
            StepUpRequiredException stepUp = new("mfa", TimeSpan.FromMinutes(10));

            Assert.Equal("step-up-required", stepUp.Code);
            Assert.Equal(("mfa", (TimeSpan?)TimeSpan.FromMinutes(10)), (stepUp.Acr, stepUp.MaxAge));
            Assert.Empty(stepUp.Arguments);
            Assert.Contains("'mfa'", stepUp.Message, StringComparison.Ordinal);
            Assert.Null(new StepUpRequiredException("mfa", null).MaxAge);
            _ = Assert.Throws<ArgumentException>(() => new StepUpRequiredException("", null));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new StepUpRequiredException("mfa", TimeSpan.Zero));
        }

        [Fact]
        public async Task A_tenant_scope_disposes_what_it_owns_once()
        {
            CountingOwner owner = new();
            ServiceProviderStub services = new();
            TenantScope scope = new(services, owner);

            Assert.Same(services, scope.Services);
            Assert.Equal(0, owner.Disposals);

            await scope.DisposeAsync();
            await scope.DisposeAsync();

            Assert.Equal(1, owner.Disposals);
            _ = Assert.Throws<ArgumentNullException>(() => new TenantScope(null!, owner));
            _ = Assert.Throws<ArgumentNullException>(() => new TenantScope(services, null!));
        }

        [Theory]
        [InlineData(typeof(IRequestContextInitializer))]
        [InlineData(typeof(ISessionTerminationListener))]
        public void Every_registration_of_an_initializer_and_a_session_listener_is_kept(Type serviceType)
        {
            Assert.NotNull(serviceType.GetCustomAttribute<CollectionServiceAttribute>());
        }

        /// <summary>An owner that counts its disposals.</summary>
        private sealed class CountingOwner : IAsyncDisposable
        {
            /// <summary>How many times the owner was disposed.</summary>
            public int Disposals { get; private set; }

            /// <inheritdoc />
            public ValueTask DisposeAsync()
            {
                Disposals++;
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>A service provider that holds nothing.</summary>
        private sealed class ServiceProviderStub : IServiceProvider
        {
            /// <inheritdoc />
            public object? GetService(Type serviceType)
            {
                return null;
            }
        }

        /// <summary>A calendar system that is only its code.</summary>
        /// <param name="code">The code.</param>
        private sealed class StubCalendarSystem(string code) : ICalendarSystem
        {
            /// <inheritdoc />
            public string Code => code;

            /// <inheritdoc />
            public Calendar Calendar { get; } = new GregorianCalendar();

            /// <inheritdoc />
            public int MonthCount => 12;

            /// <inheritdoc />
            public DateOnly MinSupported => DateOnly.MinValue;

            /// <inheritdoc />
            public DateOnly MaxSupported => DateOnly.MaxValue;

            /// <inheritdoc />
            public (int Year, int Month, int Day) Decompose(DateOnly date)
            {
                return (date.Year, date.Month, date.Day);
            }

            /// <inheritdoc />
            public string Format(DateOnly date, CultureInfo culture, DateStyle style)
            {
                return string.Empty;
            }

            /// <inheritdoc />
            public string Format(DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture, DateStyle style, bool includeTime = true)
            {
                return string.Empty;
            }

            /// <inheritdoc />
            public string MonthName(int month, CultureInfo culture, bool abbreviated)
            {
                return string.Empty;
            }

            /// <inheritdoc />
            public DateOnly? TryParse(string text, CultureInfo culture)
            {
                return null;
            }
        }
    }
}
