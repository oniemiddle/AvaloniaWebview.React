# Avalonia.React.SourceGenerator

Install this analyzer package into an Avalonia.React host project. Exported TypeScript interfaces in `.d.ts` files under the project are translated into C# API interfaces during compilation.

```ts
// @namespace MyApp.Frontend
export interface HostApi {
  getGreeting(name: string): Promise<string>;
  saveCount(value: number): Promise<void>;
  findUsers(limit: number, filter?: string): Promise<string[]>;
}
```

The example generates `IHostApi` in `MyApp.Frontend`, with `Task<string> GetGreetingAsync(string name)`, `Task SaveCountAsync(double value)`, and `Task<IReadOnlyList<string>> FindUsersAsync(double limit, string? filter = default)` members.

Supported syntax: single-line and multi-line method signatures, interface inheritance (`export interface HostApi extends BaseApi`), optional parameters, `Promise<T>`/`PromiseLike<T>` returns, primitive types, arrays (`T[]`, `Array<T>`, `ReadonlyArray<T>`), `Record<string, T>`, `Date`, `ArrayBuffer`/`Uint8Array`, nullable unions (`string | null`), and literal unions (`"light" | "dark"`). Types that cannot be represented are mapped to `JsonElement` and reported as `AVR0002`; unsupported members report `AVR0001`; generic interfaces are skipped with `AVR0003`.

Generated interfaces are `partial` and annotated with `[ReactApiContract]` and `[ReactApiMethod]`, so the bridge uses the TypeScript names rather than deriving them from C# identifiers. Multiple interfaces with the same name in different `.d.ts` files no longer collide.

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

The generated attributes come from the `Avalonia.React` package, so install it alongside this analyzer.
