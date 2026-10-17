# React bridge client

This directory contains the TypeScript package used by the React frontend hosted in `AvaloniaReactWebView`.

```sh
npm install ../client
```

Use `postMessage(type, payload)` to send a JSON message to .NET. Use `subscribe(type, listener)` to receive host messages and call the returned function to unsubscribe.

Use `invokeHostApi<T>(api, method, ...args)` to call a method exposed by a generated TypeScript API contract. For example, the `HostApi.getGreeting(name)` method resolves to its .NET `Task<string>` result:

```ts
const greeting = await invokeHostApi<string>("HostApi", "getGreeting", "Semi Design");
```

The message format is `{ "type": string, "payload": any JSON value }`. API calls use a request ID and return either a result or an error through the same message bridge.
