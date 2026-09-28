using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyApp.Infrastructure.External;

/// <summary>
/// Source-generated JSON metadata for the shipping client's request and response types.
/// ZeroAlloc.Rest's parameterless <c>SystemTextJsonSerializer</c> falls back to reflection,
/// which trimming and Native AOT cannot support; passing this context keeps the client
/// AOT-safe. Web defaults match that parameterless serializer's wire format: camelCase
/// names and case-insensitive reads.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ShippingQuoteResponse))]
internal sealed partial class ShippingJsonContext : JsonSerializerContext { }
