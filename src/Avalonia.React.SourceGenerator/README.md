# AvaloniaWebview.React.SourceGenerator

Install this analyzer package into an AvaloniaWebview.React host project. Exported TypeScript interfaces in `.d.ts` files under the project are translated into C# API interfaces during compilation.

```ts
// @namespace MyApp.Frontend
export interface HostApi {
  getGreeting(name: string): Promise<string>;
  saveCount(value: number): Promise<void>;
  findUsers(limit: number, filter?: string): Promise<string[]>;
}
```

The example generates `IHostApi` in `MyApp.Frontend`, with `Task<string> GetGreetingAsync(string name)`, `Task SaveCountAsync(double value)`, and `Task<IReadOnlyList<string>> FindUsersAsync(double limit, string? filter = default)` members.

Supported syntax: single-line and multi-line method signatures, interface inheritance (`export interface HostApi extends BaseApi`), optional parameters, `Promise<T>`/`PromiseLike<T>` returns, primitive types, arrays (`T[]`, `Array<T>`, `ReadonlyArray<T>`), `Record<string, T>`, `Date`, `ArrayBuffer`/`Uint8Array`, nullable unions (`string | null`), and literal unions (`"light" | "dark"`). Types that cannot be represented are mapped to `JsonElement` and reported as `AVR0002`; unsupported members report `AVR0001`; generic interfaces are skipped with `AVR0003`; unsupported enums are skipped with `AVR0004`.

### Generated DTOs and enums

Interfaces with only properties, and object `type` aliases, generate DTO classes:

```ts
// @namespace MyApp.Frontend
export enum Status {
  Active = "active",
  Disabled = "disabled",
}

export interface UserProfile {
  id: number;
  name: string;
  nickname?: string;
  status: Status;
}

export interface HostApi {
  save(profile: UserProfile): Promise<UserProfile>;
}
```

This emits:

```csharp
public enum Status
{
    [JsonStringEnumMemberName("active")]
    Active,
    // ...
}

public partial class UserProfile
{
    [JsonPropertyName("id")]
    public double Id { get; set; } = default!;
    public string? Nickname { get; set; }
    public global::MyApp.Frontend.Status Status { get; set; }
}

public partial interface IHostApi
{
    Task<global::MyApp.Frontend.UserProfile> SaveAsync(global::MyApp.Frontend.UserProfile profile);
}
```

String enums use `JsonStringEnumConverter<T>` and `JsonStringEnumMemberName`; numeric enums keep explicit numeric values. Interfaces declared with `extends` become class inheritance for DTOs. Generated DTOs and enums declared in other `.d.ts` files are resolved by unique name across namespaces; ambiguous names fall back to `JsonElement` with `AVR0002`.

Generated interfaces are `partial` and annotated with `[ReactApiContract]` and `[ReactApiMethod]`, so the bridge uses the TypeScript names rather than deriving them from C# identifiers. Multiple interfaces with the same name in different `.d.ts` files no longer collide.

### Reflection-free registration

Each API contract also generates a `{TypeScriptName}Bridge` static class. Register the implementation through it to avoid reflection-based method discovery:

```csharp
HostApiBridge.Register(webView, new HostApi());
```

The helper includes methods inherited from base API interfaces and passes the cancellation token when it is enabled. Primitive arguments are read directly from the bridge's `JsonElement` arguments; DTO, enum, and collection arguments use `context.JsonSerializerOptions`. For trimmed/Native AOT applications, assign a source-generated `JsonSerializerContext`:

```csharp
[JsonSerializable(typeof(UserProfile))]
internal partial class AppJsonContext : JsonSerializerContext
{
}

ReactJsonSerializerDefaults.Options = AppJsonContext.Default.Options;
```

The reflection-based `RegisterApi<TApi>` overload remains available and is annotated for trimming/AOT analyzers.

### Cancellation tokens

Set `<AvaloniaReactGenerateCancellationToken>true</AvaloniaReactGenerateCancellationToken>` in the host project to append a trailing cancellation parameter to every generated method:

```csharp
Task<string> GetGreetingAsync(string name, global::System.Threading.CancellationToken cancellationToken = default);
```

The runtime cancels the token when the React caller aborts or times out, when the page navigates, or when the WebView shuts down. The option is disabled by default so existing interfaces and implementations do not change.

Keep the contract in the React project and reference the same file from the .NET project:

```xml
<ItemGroup>
  <AdditionalFiles Include="..\react-app\src\FrontendApi.d.ts" />
</ItemGroup>
```

Implement the generated interface in the host and register it with the WebView:

```csharp
webView.RegisterApi<IHostApi>(new HostApi());
```

Call it from React through the typed proxy from `@avalonia-react/bridge`:

```ts
import { createHostApiProxy } from "@avalonia-react/bridge";
import type { HostApi } from "./FrontendApi";

const hostApi = createHostApiProxy<HostApi>("HostApi");
const greeting = await hostApi.getGreeting("Ada");
const users = await hostApi.findUsers(10);
```

The bridge returns the .NET task result or rejects with its error message. `invokeHostApi<string>("HostApi", "getGreeting", "Ada")` remains available for code that cannot use the proxy.

The generated attributes come from the `AvaloniaWebview.React` package, so install it alongside this analyzer.
