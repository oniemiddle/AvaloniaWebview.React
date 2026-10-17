# Avalonia.React.SourceGenerator

Install this analyzer package into an Avalonia.React host project. Exported TypeScript interfaces in `.d.ts` files under the project are translated into C# API interfaces during compilation.

```ts
// @namespace MyApp.Frontend
export interface HostApi {
  getGreeting(name: string): Promise<string>;
  saveCount(value: number): Promise<void>;
}
```

The example generates `IHostApi` in `MyApp.Frontend`, with `Task<string> GetGreetingAsync(string name)` and `Task SaveCountAsync(double value)` members. `string`, `number`, `boolean`, `void`, `object`, `unknown`, `any`, and array types are supported. Other types are represented by `JsonElement`.

Implement the generated interface in the host and register it with the WebView:

```csharp
webView.RegisterApi<IHostApi>(new HostApi());
```

Call it from React with `invokeHostApi<string>("HostApi", "getGreeting", "Ada")` from `@avalonia-react/bridge`. The bridge returns the .NET task result or rejects with its error message.
