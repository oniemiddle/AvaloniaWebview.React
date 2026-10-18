using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace AvaloniaWebview.React.SourceGenerator.Tests;

public sealed class TypeScriptApiGeneratorTests
{
    private const string AttributeStubs = """
        namespace AvaloniaWebview.React
        {
            [System.AttributeUsage(System.AttributeTargets.Interface)]
            public sealed class ReactApiContractAttribute : System.Attribute
            {
                public ReactApiContractAttribute(string name) { Name = name; }
                public string Name { get; }
            }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class ReactApiMethodAttribute : System.Attribute
            {
                public ReactApiMethodAttribute(string name) { Name = name; }
                public string Name { get; }
            }

            public delegate System.Threading.Tasks.Task<object?> ReactApiMethodHandler(ReactApiInvocationContext context);

            public sealed class ReactApiInvocationContext
            {
                public System.Collections.Generic.IReadOnlyList<System.Text.Json.JsonElement> Arguments =>
                    throw new System.NotImplementedException();
                public System.Threading.CancellationToken CancellationToken => default;
                public System.Text.Json.JsonSerializerOptions JsonSerializerOptions => new();
            }

            public sealed class AvaloniaReactWebView
            {
                public void RegisterApi(
                    string apiName,
                    System.Collections.Generic.IReadOnlyDictionary<string, ReactApiMethodHandler> methods)
                {
                }
            }

            public static class ReactJsonSerializerDefaults
            {
                public static void AddTypeInfoResolver(
                    System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver resolver)
                {
                }
            }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path))
    ];

    [Fact]
    public void Generates_namespace_interface_and_async_methods()
    {
        var run = Generate("""
            // @namespace MyApp.Frontend
            export interface HostApi {
              getGreeting(name: string): Promise<string>;
              saveCount(value: number): Promise<void>;
            }
            """);

        var source = run.SingleSource();
        Assert.Contains("namespace MyApp.Frontend;", source);
        Assert.Contains("ReactApiContractAttribute(\"HostApi\")", source);
        Assert.Contains("ReactApiMethodAttribute(\"getGreeting\")", source);
        Assert.Contains("Task<string> GetGreetingAsync(string name);", source);
        Assert.Contains("Task SaveCountAsync(double value);", source);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Supports_multiline_method_signatures()
    {
        var run = Generate("""
            export interface HostApi {
              getGreeting(
                name: string,
              ): Promise<string>;
            }
            """);

        Assert.Contains("Task<string> GetGreetingAsync(string name);", run.SingleSource());
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Supports_members_separated_only_by_newlines()
    {
        var source = Generate("""
            export interface HostApi {
              first(): Promise<void>
              second(value: string): Promise<string>
            }
            """).SingleSource();

        Assert.Contains("Task FirstAsync();", source);
        Assert.Contains("Task<string> SecondAsync(string value);", source);
    }

    [Fact]
    public void Generates_partial_interfaces_and_base_types()
    {
        var run = Generate("""
            export interface BaseApi {
              ping(): Promise<void>;
            }

            export interface HostApi extends BaseApi {
              pong(): Promise<void>;
            }
            """);

        var sources = string.Join("\n", run.Sources());
        Assert.Contains("public partial interface IBaseApi", sources);
        Assert.Contains("public partial interface IHostApi : IBaseApi", sources);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Preserves_original_method_names_for_the_bridge()
    {
        var source = Generate("""
            export interface HostApi {
              loadAsync(): Promise<void>;
              getGreeting(name: string): Promise<string>;
            }
            """).SingleSource();

        Assert.Contains("ReactApiMethodAttribute(\"loadAsync\")", source);
        Assert.Contains("Task LoadAsync();", source);
        Assert.Contains("ReactApiMethodAttribute(\"getGreeting\")", source);
        Assert.Contains("Task<string> GetGreetingAsync(string name);", source);
    }

    [Fact]
    public void Supports_optional_parameters_and_nullable_union_types()
    {
        var source = Generate("""
            export interface HostApi {
              greet(name: string, title?: string, theme: "light" | "dark" | null): Promise<string>;
            }
            """).SingleSource();

        Assert.Contains("string? title = default", source);
        Assert.Contains("string? theme", source);
    }

    [Fact]
    public void Maps_arrays_records_and_dates()
    {
        var source = Generate("""
            export interface HostApi {
              save(names: string[], map: Record<string, number>, created: Date): Promise<void>;
            }
            """).Sources().Single(item => item.Contains("public partial interface IHostApi"));

        Assert.Contains("IReadOnlyList<string> names", source);
        Assert.Contains("IReadOnlyDictionary<string, double> map", source);
        Assert.Contains("DateTimeOffset created", source);
    }

    [Fact]
    public void Reports_unsupported_members_and_keeps_supported_ones()
    {
        var run = Generate("""
            export interface HostApi {
              readonly name: string;
              getGreeting(name: string): Promise<string>;
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "AVR0001");
        Assert.Contains("GetGreetingAsync", run.SingleSource());
    }

    [Fact]
    public void Reports_object_literal_types_and_maps_them_to_json_element()
    {
        var run = Generate("""
            export interface HostApi {
              save(value: { id: number }): Promise<void>;
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "AVR0002");
        Assert.Contains("JsonElement value", run.SingleSource());
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Reports_unknown_referenced_types_as_json_element()
    {
        var run = Generate("""
            export interface HostApi {
              save(user: User): Promise<void>;
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "AVR0002");
        Assert.Contains("JsonElement user", run.SingleSource());
    }

    [Fact]
    public void Generates_unique_hints_for_interfaces_with_the_same_name()
    {
        var run = Generate(
            """
            export interface HostApi {
              first(): Promise<void>;
            }
            """,
            """
            export interface HostApi {
              second(): Promise<void>;
            }
            """);

        Assert.Equal(2, run.Results.SelectMany(result => result.GeneratedSources).Count());
        Assert.Contains("FirstAsync", run.Sources().First());
        Assert.Contains("SecondAsync", run.Sources().Last());
    }

    [Fact]
    public void Generates_a_reflection_free_registration_helper()
    {
        var run = Generate("""
            // @namespace MyApp.Frontend
            export interface HostApi {
              getGreeting(name: string): Promise<string>;
              saveCount(value: number): Promise<void>;
              findUsers(limit: number, filter?: string): Promise<string[]>;
            }
            """);

        var source = run.Sources().Single(item => item.Contains("public partial interface IHostApi"));
        Assert.Contains("public static partial class HostApiBridge", source);
        Assert.Contains("public static void Register(", source);
        Assert.Contains("global::AvaloniaWebview.React.AvaloniaReactWebView webView", source);
        Assert.Contains("global::MyApp.Frontend.IHostApi implementation", source);
        Assert.Contains("[\"getGreeting\"] = async context =>", source);
        Assert.Contains("var name = context.Arguments[0].GetString()!;", source);
        Assert.Contains("return await implementation.GetGreetingAsync(name).ConfigureAwait(false);", source);
        Assert.Contains("var filter = context.Arguments.Count > 1 ?", source);
        Assert.Contains("await implementation.SaveCountAsync(value).ConfigureAwait(false);", source);
        Assert.Contains("webView.RegisterApi(apiName ?? \"HostApi\",", source);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Registration_helper_includes_inherited_interface_methods()
    {
        var run = Generate("""
            export interface BaseApi {
              baseMethod(): Promise<void>;
            }

            export interface HostApi extends BaseApi {
              childMethod(): Promise<void>;
            }
            """);

        var source = run.Sources().Single(item => item.Contains("public partial interface IHostApi"));
        Assert.Contains("[\"baseMethod\"] = async context =>", source);
        Assert.Contains("[\"childMethod\"] = async context =>", source);
        Assert.Contains("implementation.BaseMethodAsync()", source);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Registration_helper_passes_the_cancellation_token()
    {
        var run = Generate(
            """
            export interface HostApi {
              getGreeting(name: string): Promise<string>;
            }
            """,
            emitCancellationToken: true);

        Assert.Contains(
            "return await implementation.GetGreetingAsync(name, context.CancellationToken).ConfigureAwait(false);",
            run.SingleSource());
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Adds_a_cancellation_token_when_the_build_property_is_enabled()
    {
        var run = Generate(
            """
            export interface HostApi {
              getGreeting(name: string): Promise<string>;
            }
            """,
            emitCancellationToken: true);

        var source = run.SingleSource();
        Assert.Contains(
            "Task<string> GetGreetingAsync(string name, global::System.Threading.CancellationToken cancellationToken = default);",
            source);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Omits_the_cancellation_token_by_default()
    {
        var source = Generate("""
            export interface HostApi {
              getGreeting(name: string): Promise<string>;
            }
            """).SingleSource();

        Assert.DoesNotContain("CancellationToken", source);
    }

    [Fact]
    public void Generates_string_enums_and_uses_them_in_api_contracts()
    {
        var run = Generate("""
            // @namespace MyApp.Frontend
            export enum Status {
              Active = "active",
              Disabled = "disabled",
            }

            export interface HostApi {
              getStatus(): Promise<Status>;
              setStatus(status: Status): Promise<void>;
            }
            """);

        var sources = run.Sources().ToArray();
        Assert.Equal(3, sources.Length);
        var enumSource = Assert.Single(sources, source => source.Contains("public enum Status"));
        Assert.Contains("JsonStringEnumConverter<Status>", enumSource);
        Assert.Contains("JsonStringEnumMemberName(\"active\")", enumSource);
        Assert.Contains("Active,", enumSource);

        var apiSource = Assert.Single(sources, source => source.Contains("public partial interface IHostApi"));
        Assert.Contains("Task<global::MyApp.Frontend.Status> GetStatusAsync();", apiSource);
        Assert.Contains("Task SetStatusAsync(global::MyApp.Frontend.Status status);", apiSource);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Generates_numeric_enums_with_explicit_values()
    {
        var run = Generate("""
            export enum Priority {
              Low = 1,
              High = 5,
            }
            """);

        var source = run.Sources().Single(item => item.Contains("public enum Priority"));
        Assert.Contains("public enum Priority", source);
        Assert.Contains("Low = 1,", source);
        Assert.Contains("High = 5,", source);
        Assert.DoesNotContain("JsonStringEnumConverter", source);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Generates_dto_classes_with_json_property_names_and_local_types()
    {
        var run = Generate("""
            // @namespace MyApp.Frontend
            export enum Status {
              Active = "active",
            }

            export interface UserProfile {
              id: number;
              name: string;
              nickname?: string;
              tags: string[];
              status: Status;
            }

            export interface HostApi {
              save(profile: UserProfile): Promise<UserProfile>;
            }
            """);

        var sources = run.Sources().ToArray();
        var dtoSource = Assert.Single(sources, source => source.Contains("public partial class UserProfile"));
        Assert.Contains("JsonPropertyName(\"id\")", dtoSource);
        Assert.Contains("public double Id { get; set; } = default!;", dtoSource);
        Assert.Contains("public string Name { get; set; } = default!;", dtoSource);
        Assert.Contains("public string? Nickname { get; set; }", dtoSource);
        Assert.Contains("public global::System.Collections.Generic.IReadOnlyList<string> Tags { get; set; }", dtoSource);
        Assert.Contains("public global::MyApp.Frontend.Status Status { get; set; }", dtoSource);

        var apiSource = Assert.Single(sources, source => source.Contains("public partial interface IHostApi"));
        Assert.Contains(
            "Task<global::MyApp.Frontend.UserProfile> SaveAsync(global::MyApp.Frontend.UserProfile profile);",
            apiSource);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Generates_dto_classes_from_object_type_aliases()
    {
        var run = Generate("""
            export type Settings = {
              theme: string;
              compact?: boolean;
            };

            export interface HostApi {
              save(settings: Settings): Promise<void>;
            }
            """);

        var sources = run.Sources().ToArray();
        Assert.Contains(sources, source => source.Contains("public partial class Settings"));
        var apiSource = Assert.Single(sources, source => source.Contains("public partial interface IHostApi"));
        Assert.Contains("Settings settings", apiSource);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Resolves_scalar_type_aliases_without_emitting_a_type()
    {
        var run = Generate("""
            export type UserId = string;

            export interface HostApi {
              find(id: UserId): Promise<void>;
            }
            """);

        var source = run.SingleSource();
        Assert.Contains("Task FindAsync(string id);", source);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Generates_dto_inheritance_for_interface_extends()
    {
        var run = Generate("""
            export interface BaseEntity {
              id: number;
            }

            export interface User extends BaseEntity {
              name: string;
            }
            """);

        var sources = run.Sources().ToArray();
        Assert.Contains(sources, source => source.Contains("public partial class BaseEntity"));
        Assert.Contains(sources, source => source.Contains("public partial class User : BaseEntity"));
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Generates_self_contained_json_support_for_dtos_enums_and_collections()
    {
        var run = Generate("""
            // @namespace MyApp.Frontend
            export enum Status {
              Active = "active",
              Disabled = "disabled",
            }

            export interface UserProfile {
              id: number;
              name: string;
              nickname?: string;
              tags: string[];
              status: Status;
              score?: number;
            }

            export interface HostApi {
              save(profile: UserProfile): Promise<UserProfile[]>;
            }
            """);

        var support = run.Sources().Single(source =>
            source.Contains("AvaloniaReactGeneratedTypeInfoResolver"));
        Assert.Contains("UserProfileJsonConverter", support);
        Assert.Contains("StatusJsonConverter", support);
        Assert.Contains("ReactListConverter<global::MyApp.Frontend.UserProfile>", support);
        Assert.Contains("ReactListConverter<string>", support);
        Assert.Contains("ReactJsonSerializerDefaults.AddTypeInfoResolver", support);
        Assert.Contains("ModuleInitializer", support);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Skips_mixed_enum_values_with_a_diagnostic()
    {
        var run = Generate("""
            export enum Broken {
              Text = "text",
              Number = 1,
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "AVR0004");
        Assert.Empty(run.Results.SelectMany(result => result.GeneratedSources));
    }

    [Fact]
    public void Resolves_types_declared_in_another_file()
    {
        var run = Generate(
            """
            // @namespace MyApp.Frontend
            export interface User {
              id: number;
            }
            """,
            """
            // @namespace MyApp.Frontend
            export interface HostApi {
              save(user: User): Promise<User>;
            }
            """);

        var sources = run.Sources().ToArray();
        Assert.Contains(sources, source => source.Contains("public partial class User"));
        var apiSource = Assert.Single(sources, source => source.Contains("public partial interface IHostApi"));
        Assert.Contains("global::MyApp.Frontend.User", apiSource);
        Assert.DoesNotContain("JsonElement", apiSource);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Resolves_types_across_namespaces()
    {
        var run = Generate(
            """
            // @namespace Shared
            export enum Status {
              Active = "active",
            }
            """,
            """
            // @namespace MyApp.Frontend
            export interface HostApi {
              getStatus(): Promise<Status>;
            }
            """);

        var sources = run.Sources().ToArray();
        var apiSource = Assert.Single(sources, source => source.Contains("public partial interface IHostApi"));
        Assert.Contains("Task<global::Shared.Status> GetStatusAsync();", apiSource);
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Resolves_dto_inheritance_across_files()
    {
        var run = Generate(
            """
            // @namespace MyApp.Frontend
            export interface BaseEntity {
              id: number;
            }
            """,
            """
            // @namespace MyApp.Frontend
            export interface User extends BaseEntity {
              name: string;
            }
            """);

        Assert.Contains(run.Sources(), source => source.Contains("public partial class User : BaseEntity"));
        Assert.Empty(run.CompilationErrors());
    }

    [Fact]
    public void Skips_generic_interfaces_with_a_diagnostic()
    {
        var run = Generate("""
            export interface HostApi<T> {
              get(value: T): Promise<void>;
            }
            """);

        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Id == "AVR0003");
        Assert.Empty(run.Results.SelectMany(result => result.GeneratedSources));
    }

    private static GenerationRun Generate(params string[] sources) => Generate(sources, emitCancellationToken: false);

    private static GenerationRun Generate(string source, bool emitCancellationToken) =>
        Generate([source], emitCancellationToken);

    private static GenerationRun Generate(string[] sources, bool emitCancellationToken)
    {
        var files = sources
            .Select((source, index) => (AdditionalText)new InMemoryAdditionalText($"C:\\app\\FrontendApi{index}.d.ts", source))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [CSharpSyntaxTree.ParseText(AttributeStubs)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new TypeScriptApiGenerator().AsSourceGenerator()],
            additionalTexts: files,
            optionsProvider: new TestOptionsProvider(emitCancellationToken));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return new GenerationRun(driver.GetRunResult(), outputCompilation);
    }

    private sealed class TestOptionsProvider(bool emitCancellationToken) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options = new TestOptions(new Dictionary<string, string>
        {
            ["build_property.AvaloniaReactGenerateCancellationToken"] = emitCancellationToken ? "true" : "false",
        });

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class TestOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed class GenerationRun(GeneratorDriverRunResult result, Compilation compilation)
    {
        public ImmutableArray<GeneratorRunResult> Results { get; } = result.Results;

        public ImmutableArray<Diagnostic> Diagnostics { get; } = result.Diagnostics;

        public Compilation Compilation { get; } = compilation;

        public IEnumerable<string> Sources() => Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.SourceText.ToString());

        public string SingleSource() => Assert.Single(Sources());

        public IEnumerable<Diagnostic> CompilationErrors() => Compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
