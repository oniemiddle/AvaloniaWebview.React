using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Avalonia.React.SourceGenerator.Tests;

public sealed class TypeScriptApiGeneratorTests
{
    private const string AttributeStubs = """
        namespace Avalonia.React
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
            """).SingleSource();

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
