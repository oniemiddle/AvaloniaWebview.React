# Avalonia.React.Vite

Install this extension alongside `Avalonia.React` to start a Vite frontend when an Avalonia host is built in Debug configuration. It starts Node directly with Vite's CLI, installs dependencies with `npm ci` when `node_modules` is missing, and waits until the configured URL accepts a connection.

Configure the project directory and URL in the host `.csproj`:

```xml
<PropertyGroup>
  <AvaloniaReactDevServerDirectory>$(MSBuildProjectDirectory)\..\ClientApp</AvaloniaReactDevServerDirectory>
  <AvaloniaReactDevServerUrl>http://127.0.0.1:5173/</AvaloniaReactDevServerUrl>
</PropertyGroup>
```

Node.js and npm must be installed. Set `AvaloniaReactAutoStart` to `false` to disable automatic launch. The process stays alive after the build so the desktop app can connect, and `AvaloniaReactWebView` stops the server process tree when the host exits.
