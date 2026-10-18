# Trimming and Native AOT

The library is trim- and AOT-friendly; generated contracts avoid reflection for JSON metadata and API dispatch.

## Trimming and Native AOT

The library is marked `IsTrimmable` and `IsAotCompatible`; its trim and AOT analyzers build cleanly. Prefer the generated registration helper, which invokes the implementation through delegates instead of `MethodInfo` reflection:

```csharp
HostApiBridge.Register(webView, new HostApi());
```

Generated handlers read `string`, `number`, `boolean`, `DateTimeOffset`, `byte[]`, and `JsonElement` arguments directly from `JsonElement`, and the bridge writes primitive/`JsonElement` results without reflection. The generator also emits converters and a type-info resolver for generated DTOs, enums, lists, dictionaries, nullable values, and the types used by generated contracts. A module initializer installs that resolver into `ReactJsonSerializerDefaults.Options`, so most trimmed/Native AOT apps need no JSON setup at all.

For custom payload types that are not declared in a generated `.d.ts` contract (for example an arbitrary `PostMessageAsync<T>` or `InvokeReactAsync<T>` type), provide a source-generated context:

```csharp
[JsonSerializable(typeof(CustomMessage))]
internal partial class AppJsonContext : JsonSerializerContext
{
}

ReactJsonSerializerDefaults.Options = AppJsonContext.Default.Options;
// Or per view: webView.BridgeJsonSerializerOptions = AppJsonContext.Default.Options;
```

Setting `ReactJsonSerializerDefaults.Options` keeps the generated resolver as a fallback, so generated DTO/enum contracts and custom context types can coexist. The reflection-based `RegisterApi<TApi>` path remains annotated with `RequiresUnreferencedCode`/`RequiresDynamicCode`; use `HostApiBridge.Register` for trimming/AOT. Embedded assets, the loopback server, and WebView hosting have no additional trim requirements. The sample's primitive API and a generated DTO/enum/list contract were verified with `PublishTrimmed=true` and reflection-based serialization disabled, and `samples/ReactHost` publishes end to end with Native AOT (`eng/publish-aot.ps1`).

