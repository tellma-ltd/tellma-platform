// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The JSON the job contract reads and writes: the size guard on the arguments of a label, a
    ///     progress message or an error, and the tolerant reading of a job's arguments and checkpoint.
    ///     Shared by the contract types here and by the runtime's progress, completion and enqueue, so
    ///     every caller is held to one rule.
    /// </summary>
    internal static class JobJson
    {
        /// <summary>
        ///     The platform's JSON rules with every member optional on the way in: a missing member,
        ///     a <c>required</c> one included, keeps its default rather than failing the read, so a
        ///     queued job outlives an additive change to its handler's argument or checkpoint type.
        /// </summary>
        public static JsonSerializerOptions TolerantOptions { get; } = CreateTolerantOptions();

        /// <summary>
        ///     Checks a resource key and serialises its arguments under the platform's JSON rules,
        ///     refusing a key or arguments too wide for the job table's columns.
        /// </summary>
        /// <param name="key">The resource key: a label, a progress message key or an error code.</param>
        /// <param name="arguments">The arguments.</param>
        /// <param name="parameterName">The parameter the key and arguments arrived in, named in the exception.</param>
        /// <returns>The arguments as a JSON object; <see langword="null" /> when there are none.</returns>
        /// <exception cref="ArgumentException">
        ///     The key is longer than <see cref="JobLimits.MaxKeyLength" /> characters, an argument
        ///     cannot be serialised, or the arguments take more than
        ///     <see cref="JobLimits.MaxArgumentsLength" /> characters once serialised.
        /// </exception>
        public static string? SerializeArguments(string key, IReadOnlyDictionary<string, object?> arguments, string parameterName)
        {
            // --- The key fits its column ---
            if (key.Length > JobLimits.MaxKeyLength)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"The key '{key}' has {key.Length} characters; a job's keys have at most {JobLimits.MaxKeyLength}."),
                    parameterName);
            }

            // --- No arguments store nothing ---
            if (arguments.Count == 0)
            {
                return null;
            }

            // --- Serialise, then hold the text to its column ---
            string json;
            try
            {
                json = JsonSerializer.Serialize(arguments, TellmaJson.Options);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new ArgumentException($"The arguments of '{key}' cannot be serialised as JSON: {exception.Message}", parameterName, exception);
            }

            return json.Length <= JobLimits.MaxArgumentsLength
                ? json
                : throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"The arguments of '{key}' take {json.Length} characters once serialised; a job stores at most {JobLimits.MaxArgumentsLength}."),
                    parameterName);
        }

        /// <summary>Checks and serialises a progress message or a label; see <see cref="SerializeArguments(string, IReadOnlyDictionary{string, object}, string)" />.</summary>
        /// <param name="message">The message.</param>
        /// <param name="parameterName">The parameter it arrived in.</param>
        /// <returns>The arguments as a JSON object; <see langword="null" /> when there are none.</returns>
        /// <exception cref="ArgumentException">The message is too wide for the job table.</exception>
        public static string? SerializeArguments(JobMessage message, string parameterName)
        {
            return SerializeArguments(message.Key, message.Arguments, parameterName);
        }

        /// <summary>Checks and serialises an error; see <see cref="SerializeArguments(string, IReadOnlyDictionary{string, object}, string)" />.</summary>
        /// <param name="error">The error.</param>
        /// <param name="parameterName">The parameter it arrived in.</param>
        /// <returns>The arguments as a JSON object; <see langword="null" /> when there are none.</returns>
        /// <exception cref="ArgumentException">The error is too wide for the job table.</exception>
        public static string? SerializeArguments(JobError error, string parameterName)
        {
            return SerializeArguments(error.Code, error.Arguments, parameterName);
        }

        /// <summary>
        ///     Reads a job's arguments: tolerantly when there is JSON, and when there is none, a fresh
        ///     instance of the type's empty shape.
        /// </summary>
        /// <typeparam name="T">The arguments' type.</typeparam>
        /// <param name="json">The stored JSON, or <see langword="null" />.</param>
        /// <returns>
        ///     The arguments. With no JSON: <see langword="null" /> for a nullable value type, the default
        ///     for any other value type, a fresh instance read from <c>{}</c> for an object or dictionary
        ///     type, an empty collection read from <c>[]</c> for a collection type, and
        ///     <see langword="null" /> for any other type, such as <see cref="string" />.
        /// </returns>
        /// <exception cref="JsonException">The JSON does not fit the type.</exception>
        /// <exception cref="NotSupportedException">The type cannot be created from JSON, such as an abstract type read without JSON.</exception>
        public static T ReadArguments<T>(string? json)
        {
            if (json is not null)
            {
                return JsonSerializer.Deserialize<T>(json, TolerantOptions)!;
            }

            // A nullable value type reads as null; any other value type as its default.
            if (typeof(T).IsValueType)
            {
                return default!;
            }

            // A reference type reads as the empty shape its contract has, so a handler never
            // null-checks arguments it was enqueued without.
            JsonTypeInfoKind kind = TolerantOptions.GetTypeInfo(typeof(T)).Kind;
            if (kind is JsonTypeInfoKind.Object or JsonTypeInfoKind.Dictionary)
            {
                return JsonSerializer.Deserialize<T>("{}", TolerantOptions)!;
            }

            // A collection reads as an empty one; a value read as a JSON primitive, such as a string,
            // has no empty shape and reads as null.
            return kind is JsonTypeInfoKind.Enumerable ? JsonSerializer.Deserialize<T>("[]", TolerantOptions)! : default!;
        }

        /// <summary>Reads a job's checkpoint tolerantly.</summary>
        /// <typeparam name="T">The checkpoint's type.</typeparam>
        /// <param name="json">The stored JSON, or <see langword="null" />.</param>
        /// <returns>The checkpoint; the type's default when there is none.</returns>
        /// <exception cref="JsonException">The JSON does not fit the type.</exception>
        public static T? ReadState<T>(string? json)
        {
            return json is null ? default : JsonSerializer.Deserialize<T>(json, TolerantOptions);
        }

        /// <summary>Copies the platform's options and makes every member optional on the way in.</summary>
        /// <returns>The read-only options.</returns>
        private static JsonSerializerOptions CreateTolerantOptions()
        {
            JsonSerializerOptions options = new(TellmaJson.Options);
            options.TypeInfoResolver = options.TypeInfoResolver!.WithAddedModifier(static typeInfo =>
            {
                // Required members and required constructor parameters become optional, which is
                // what "missing members defaulted" means for a type written with `required`.
                foreach (JsonPropertyInfo property in typeInfo.Properties)
                {
                    property.IsRequired = false;
                }
            });
            options.MakeReadOnly();
            return options;
        }
    }
}
