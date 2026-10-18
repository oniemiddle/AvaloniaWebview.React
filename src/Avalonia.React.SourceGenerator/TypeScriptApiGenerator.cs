using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Avalonia.React.SourceGenerator;

/// <summary>
/// Generates C# API interfaces, DTO classes, and enums from exported TypeScript declarations in
/// <c>.d.ts</c> files.
/// </summary>
[Generator]
public sealed class TypeScriptApiGenerator : IIncrementalGenerator
{
    private static readonly Regex NamespacePattern = new(
        @"//\s*@namespace\s+(?<name>[A-Za-z_][\w.]*)",
        RegexOptions.Compiled);

    private static readonly Regex InterfacePattern = new(
        @"\bexport\s+interface\s+(?<name>[A-Za-z_$][\w$]*)(?<generics>\s*<[^>{}]*>)?(?:\s+extends\s+(?<bases>[^{]+?))?\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex EnumPattern = new(
        @"\bexport\s+(?:const\s+)?enum\s+(?<name>[A-Za-z_$][\w$]*)\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex TypeAliasPattern = new(
        @"\bexport\s+type\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*",
        RegexOptions.Compiled);

    private static readonly Regex MethodPattern = new(
        @"^\s*(?<name>[A-Za-z_$][\w$]*)\s*(?<optional>\?)?\s*\((?<args>.*)\)\s*:\s*(?<result>.+?)\s*;?\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex PropertyPattern = new(
        @"^\s*(?:readonly\s+)?(?<name>[A-Za-z_$][\w$]*)\s*(?<optional>\?)?\s*:\s*(?<type>.+?)\s*;?\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ParameterPattern = new(
        @"^\s*(?<rest>\.\.\.)?(?<name>[A-Za-z_$][\w$]*)\s*(?<optional>\?)?\s*:\s*(?<type>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex EnumMemberPattern = new(
        @"^\s*(?<name>[A-Za-z_$][\w$]*)\s*(?:=\s*(?<value>.+?))?\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly DiagnosticDescriptor UnsupportedMember = new(
        id: "AVR0001",
        title: "Unsupported TypeScript member",
        messageFormat: "The member '{0}' on '{1}' is not supported and was skipped",
        category: "Avalonia.React",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TypeFallback = new(
        id: "AVR0002",
        title: "TypeScript type mapped to JsonElement",
        messageFormat: "The TypeScript type '{0}' is not supported and maps to System.Text.Json.JsonElement",
        category: "Avalonia.React",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericInterface = new(
        id: "AVR0003",
        title: "Generic TypeScript interface skipped",
        messageFormat: "The interface '{0}' declares type parameters, which are not supported",
        category: "Avalonia.React",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedEnum = new(
        id: "AVR0004",
        title: "Unsupported TypeScript enum skipped",
        messageFormat: "The enum '{0}' is not supported: {1}",
        category: "Avalonia.React",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            .Select((file, cancellationToken) => new DeclarationFile(file.Path, file.GetText(cancellationToken)?.ToString() ?? string.Empty))
            .Collect();
        var emitCancellationToken = context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
            provider.GlobalOptions.TryGetValue("build_property.AvaloniaReactGenerateCancellationToken", out var value) &&
            bool.TryParse(value, out var enabled) &&
            enabled);

        context.RegisterSourceOutput(
            declarations.Combine(emitCancellationToken),
            static (productionContext, input) => Generate(productionContext, input.Left, input.Right));
    }

    private static void Generate(
        SourceProductionContext context,
        ImmutableArray<DeclarationFile> files,
        bool emitCancellationToken)
    {
        var parsedFiles = new List<ParsedFile>();
        var fileIndex = 0;
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Text))
            {
                fileIndex++;
                continue;
            }

            var namespaceName = ParseNamespace(file.Text) ?? "Avalonia.React.Generated";
            var text = RemoveComments(file.Text);
            var declarations = ParseDeclarations(context, file, text, namespaceName);
            parsedFiles.Add(new ParsedFile(file, fileIndex++, namespaceName, declarations));
        }

        var globalTypes = BuildGlobalTypes(context, parsedFiles);
        foreach (var parsedFile in parsedFiles)
        {
            var localTypes = BuildLocalTypes(context, parsedFile.Declarations);
            var mapper = CreateMapper(context, parsedFile.File, parsedFile.NamespaceName, localTypes, globalTypes);
            var declarationIndex = 0;
            foreach (var declaration in parsedFile.Declarations)
            {
                if (declaration.Kind == DeclarationKind.Alias)
                {
                    continue;
                }

                var source = Emit(
                    declaration,
                    parsedFile.NamespaceName,
                    localTypes,
                    globalTypes,
                    mapper,
                    emitCancellationToken,
                    context);
                if (source is null)
                {
                    continue;
                }

                var hintName = $"{Sanitize(FileName(parsedFile.File.Path))}.{Sanitize(declaration.CSharpName)}.{parsedFile.Index}.{declarationIndex}.g.cs";
                context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
                declarationIndex++;
            }
        }
    }

    private static Dictionary<string, Declaration> BuildLocalTypes(
        SourceProductionContext context,
        List<Declaration> declarations)
    {
        var localTypes = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            if (localTypes.ContainsKey(declaration.TypeScriptName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedMember,
                    declaration.Location,
                    declaration.TypeScriptName,
                    "the declarations in this file"));
                continue;
            }

            localTypes.Add(declaration.TypeScriptName, declaration);
        }

        return localTypes;
    }

    private static Dictionary<string, Declaration> BuildGlobalTypes(
        SourceProductionContext context,
        List<ParsedFile> files)
    {
        var globalTypes = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var declaration in file.Declarations)
            {
                if (!globalTypes.ContainsKey(declaration.TypeScriptName))
                {
                    globalTypes.Add(declaration.TypeScriptName, declaration);
                    continue;
                }

                duplicates.Add(declaration.TypeScriptName);
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedMember,
                    declaration.Location,
                    declaration.TypeScriptName,
                    "the referenced declarations across files"));
            }
        }

        foreach (var duplicate in duplicates)
        {
            globalTypes.Remove(duplicate);
        }

        return globalTypes;
    }

    private static List<Declaration> ParseDeclarations(
        SourceProductionContext context,
        DeclarationFile file,
        string text,
        string namespaceName)
    {
        var declarations = new List<Declaration>();
        foreach (Match match in InterfacePattern.Matches(text))
        {
            var name = match.Groups["name"].Value;
            var location = CreateLocation(file.Path, match.Index);
            if (match.Groups["generics"].Success && match.Groups["generics"].Value.Length > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(GenericInterface, location, name));
                continue;
            }

            var openBrace = match.Index + match.Length - 1;
            var closeBrace = FindClosingBrace(text, openBrace);
            if (closeBrace < 0)
            {
                continue;
            }

            var declaration = new Declaration(name, namespaceName, location);
            foreach (var baseName in SplitTopLevel(match.Groups["bases"].Value, ','))
            {
                var trimmed = baseName.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (!Regex.IsMatch(trimmed, @"^[A-Za-z_$][\w$]*$"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedMember, location, "extends " + trimmed, name));
                    continue;
                }

                declaration.BaseNames.Add(trimmed);
            }

            var body = NormalizeMemberSeparators(text.Substring(openBrace + 1, closeBrace - openBrace - 1));
            ParseMembers(context, declaration, body, name);
            declaration.Kind = declaration.Methods.Count > 0 || declaration.Properties.Count == 0
                ? DeclarationKind.Api
                : DeclarationKind.Dto;
            declaration.CSharpName = declaration.Kind == DeclarationKind.Api
                ? ToInterfaceName(name)
                : ToPascal(name);
            if (declaration.Kind == DeclarationKind.Api)
            {
                foreach (var property in declaration.Properties)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedMember,
                        declaration.Location,
                        property.Name + ": " + property.Type,
                        name));
                }
            }

            declarations.Add(declaration);
        }

        foreach (Match match in EnumPattern.Matches(text))
        {
            var name = match.Groups["name"].Value;
            var location = CreateLocation(file.Path, match.Index);
            var openBrace = match.Index + match.Length - 1;
            var closeBrace = FindClosingBrace(text, openBrace);
            if (closeBrace < 0)
            {
                continue;
            }

            var declaration = new Declaration(name, namespaceName, location)
            {
                Kind = DeclarationKind.Enum,
                CSharpName = ToPascal(name),
            };
            if (TryParseEnum(context, declaration, text.Substring(openBrace + 1, closeBrace - openBrace - 1)))
            {
                declarations.Add(declaration);
            }
        }

        foreach (Match match in TypeAliasPattern.Matches(text))
        {
            var name = match.Groups["name"].Value;
            var location = CreateLocation(file.Path, match.Index);
            var targetStart = SkipWhitespace(text, match.Index + match.Length);
            if (targetStart < text.Length && text[targetStart] == '{')
            {
                var closeBrace = FindClosingBrace(text, targetStart);
                if (closeBrace < 0)
                {
                    continue;
                }

                var declaration = new Declaration(name, namespaceName, location)
                {
                    Kind = DeclarationKind.Dto,
                    CSharpName = ToPascal(name),
                };
                var body = NormalizeMemberSeparators(text.Substring(targetStart + 1, closeBrace - targetStart - 1));
                ParseMembers(context, declaration, body, name);
                // An object type alias is a DTO unless a member is method-like, which is unsupported.
                if (declaration.Methods.Count > 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedMember,
                        location,
                        "methods in a type alias",
                        name));
                    continue;
                }

                declaration.Kind = DeclarationKind.Dto;
                declarations.Add(declaration);
                continue;
            }

            var end = text.IndexOf(';', targetStart);
            if (end < 0)
            {
                continue;
            }

            declarations.Add(new Declaration(name, namespaceName, location)
            {
                Kind = DeclarationKind.Alias,
                CSharpName = ToPascal(name),
                AliasTarget = text.Substring(targetStart, end - targetStart).Trim(),
            });
        }

        return declarations;
    }

    private static void ParseMembers(
        SourceProductionContext context,
        Declaration declaration,
        string body,
        string ownerName)
    {
        foreach (var member in SplitMembers(body))
        {
            var trimmed = member.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var methodMatch = MethodPattern.Match(trimmed);
            if (methodMatch.Success)
            {
                var method = new RawMethod(
                    methodMatch.Groups["name"].Value,
                    methodMatch.Groups["args"].Value,
                    methodMatch.Groups["result"].Value.Trim());
                declaration.Methods.Add(method);
                continue;
            }

            var propertyMatch = PropertyPattern.Match(trimmed);
            if (propertyMatch.Success)
            {
                declaration.Properties.Add(new RawProperty(
                    propertyMatch.Groups["name"].Value,
                    propertyMatch.Groups["type"].Value.Trim(),
                    propertyMatch.Groups["optional"].Success));
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(UnsupportedMember, declaration.Location, Summarize(trimmed), ownerName));
        }
    }

    private static bool TryParseEnum(SourceProductionContext context, Declaration declaration, string body)
    {
        foreach (var member in SplitTopLevel(body, ','))
        {
            var trimmed = member.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var match = EnumMemberPattern.Match(trimmed);
            if (!match.Success)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedEnum, declaration.Location, declaration.TypeScriptName, Summarize(trimmed)));
                return false;
            }

            var memberName = match.Groups["name"].Value;
            var valueText = match.Groups["value"].Success ? match.Groups["value"].Value.Trim() : null;
            string? stringValue = null;
            int? numericValue = null;
            if (valueText is not null)
            {
                if (IsStringLiteral(valueText))
                {
                    stringValue = Unquote(valueText);
                }
                else if (int.TryParse(valueText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                {
                    numericValue = number;
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedEnum, declaration.Location, declaration.TypeScriptName, $"unsupported value '{valueText}'"));
                    return false;
                }
            }

            declaration.EnumMembers.Add(new EnumMember(memberName, stringValue, numericValue));
        }

        var stringMembers = declaration.EnumMembers.Count(member => member.StringValue is not null);
        var numericMembers = declaration.EnumMembers.Count(member => member.NumericValue is not null || member.StringValue is null);
        if (stringMembers > 0 && numericMembers > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedEnum, declaration.Location, declaration.TypeScriptName, "mixed string and numeric values are not supported"));
            return false;
        }

        if (stringMembers > 0 &&
            declaration.EnumMembers.Any(member => member.StringValue is null))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedEnum, declaration.Location, declaration.TypeScriptName, "string enums require a value for every member"));
            return false;
        }

        declaration.IsStringEnum = stringMembers > 0;
        return true;
    }

    private static string? Emit(
        Declaration declaration,
        string namespaceName,
        Dictionary<string, Declaration> localTypes,
        Dictionary<string, Declaration> globalTypes,
        TypeMapper mapper,
        bool emitCancellationToken,
        SourceProductionContext context) =>
        declaration.Kind switch
        {
            DeclarationKind.Api => EmitApi(declaration, namespaceName, localTypes, globalTypes, mapper, emitCancellationToken, context),
            DeclarationKind.Dto => EmitDto(declaration, namespaceName, localTypes, globalTypes, mapper, context),
            DeclarationKind.Enum => EmitEnum(declaration, namespaceName),
            _ => null,
        };

    private static string EmitApi(
        Declaration declaration,
        string namespaceName,
        Dictionary<string, Declaration> localTypes,
        Dictionary<string, Declaration> globalTypes,
        TypeMapper mapper,
        bool emitCancellationToken,
        SourceProductionContext context)
    {
        var builder = Header(namespaceName);
        builder.Append("[global::Avalonia.React.ReactApiContractAttribute(\"")
            .Append(EscapeString(declaration.TypeScriptName))
            .AppendLine("\")]");
        builder.Append("public partial interface ").Append(declaration.CSharpName);
        var bases = new List<string>();
        foreach (var baseName in declaration.BaseNames)
        {
            var resolved = ResolveBase(
                baseName,
                namespaceName,
                localTypes,
                globalTypes,
                DeclarationKind.Api,
                declaration,
                context);
            bases.Add(resolved ?? ToInterfaceName(baseName));
        }

        if (bases.Count > 0)
        {
            builder.Append(" : ").Append(string.Join(", ", bases.Distinct(StringComparer.Ordinal)));
        }

        builder.AppendLine();
        builder.AppendLine("{");
        foreach (var method in declaration.Methods)
        {
            var methodName = ToPascal(method.Name);
            var returnType = MapReturnType(method.ReturnType, mapper, out var isAsync);
            if (isAsync && !methodName.EndsWith("Async", StringComparison.Ordinal))
            {
                methodName += "Async";
            }

            builder.Append("    [global::Avalonia.React.ReactApiMethodAttribute(\"")
                .Append(EscapeString(method.Name))
                .AppendLine("\")]");
            builder.Append("    ").Append(returnType).Append(' ').Append(methodName).Append('(');
            var parameters = MapParameters(method, mapper);
            if (emitCancellationToken)
            {
                parameters.Add("global::System.Threading.CancellationToken cancellationToken = default");
            }

            builder.Append(string.Join(", ", parameters));
            builder.AppendLine(");");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string? EmitDto(
        Declaration declaration,
        string namespaceName,
        Dictionary<string, Declaration> localTypes,
        Dictionary<string, Declaration> globalTypes,
        TypeMapper mapper,
        SourceProductionContext context)
    {
        var builder = Header(namespaceName);
        builder.Append("public partial class ").Append(declaration.CSharpName);
        var bases = new List<string>();
        foreach (var baseName in declaration.BaseNames)
        {
            var resolved = ResolveBase(
                baseName,
                namespaceName,
                localTypes,
                globalTypes,
                DeclarationKind.Dto,
                declaration,
                context);
            if (resolved is not null)
            {
                bases.Add(resolved);
            }
        }

        if (bases.Count > 0)
        {
            builder.Append(" : ").Append(string.Join(", ", bases.Distinct(StringComparer.Ordinal)));
        }

        builder.AppendLine();
        builder.AppendLine("{");
        foreach (var property in declaration.Properties)
        {
            var propertyType = mapper.Map(property.Type);
            if (property.Optional)
            {
                propertyType = MakeNullable(propertyType);
            }

            builder.Append("    [global::System.Text.Json.Serialization.JsonPropertyName(\"")
                .Append(EscapeString(property.Name))
                .AppendLine("\")]");
            builder.Append("    public ").Append(propertyType).Append(' ').Append(ToPascal(property.Name)).Append(" { get; set; }");
            if (!property.Optional)
            {
                builder.Append(" = default!;");
            }

            builder.AppendLine();
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string? ResolveBase(
        string baseName,
        string currentNamespace,
        Dictionary<string, Declaration> localTypes,
        Dictionary<string, Declaration> globalTypes,
        DeclarationKind expectedKind,
        Declaration owner,
        SourceProductionContext context)
    {
        localTypes.TryGetValue(baseName, out var baseDeclaration);
        if (baseDeclaration is null)
        {
            globalTypes.TryGetValue(baseName, out baseDeclaration);
        }

        if (baseDeclaration is null || baseDeclaration == owner || baseDeclaration.Kind != expectedKind)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedMember,
                owner.Location,
                "extends " + baseName,
                owner.TypeScriptName));
            return null;
        }

        return string.Equals(baseDeclaration.NamespaceName, currentNamespace, StringComparison.Ordinal)
            ? baseDeclaration.CSharpName
            : "global::" + baseDeclaration.NamespaceName + "." + baseDeclaration.CSharpName;
    }

    private static string EmitEnum(Declaration declaration, string namespaceName)
    {
        var builder = Header(namespaceName);
        if (declaration.IsStringEnum)
        {
            builder.Append("[global::System.Text.Json.Serialization.JsonConverter(typeof(global::System.Text.Json.Serialization.JsonStringEnumConverter<")
                .Append(declaration.CSharpName)
                .AppendLine(">))]");
        }

        builder.Append("public enum ").Append(declaration.CSharpName).AppendLine();
        builder.AppendLine("{");
        foreach (var member in declaration.EnumMembers)
        {
            if (member.StringValue is not null)
            {
                builder.Append("    [global::System.Text.Json.Serialization.JsonStringEnumMemberName(\"")
                    .Append(EscapeString(member.StringValue))
                    .AppendLine("\")]");
            }

            builder.Append("    ").Append(ToPascal(member.Name));
            if (member.NumericValue is { } numericValue)
            {
                builder.Append(" = ").Append(numericValue.ToString(CultureInfo.InvariantCulture));
            }

            builder.AppendLine(",");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static StringBuilder Header(string namespaceName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.Append("namespace ").Append(namespaceName).AppendLine(";");
        builder.AppendLine();
        return builder;
    }

    private static List<string> MapParameters(RawMethod method, TypeMapper mapper)
    {
        var parameters = new List<string>();
        foreach (var argument in SplitTopLevel(method.Arguments, ','))
        {
            var argumentText = argument.Trim();
            if (argumentText.Length == 0 || argumentText.StartsWith("this:", StringComparison.Ordinal))
            {
                continue;
            }

            var parameterMatch = ParameterPattern.Match(argumentText);
            if (!parameterMatch.Success)
            {
                continue;
            }

            var parameterType = mapper.Map(parameterMatch.Groups["type"].Value);
            var optional = parameterMatch.Groups["optional"].Success;
            if (optional || parameterMatch.Groups["rest"].Success)
            {
                parameterType = MakeNullable(parameterType);
            }

            var parameterName = Identifier(parameterMatch.Groups["name"].Value);
            parameters.Add(optional
                ? parameterType + " " + parameterName + " = default"
                : parameterType + " " + parameterName);
        }

        return parameters;
    }

    private static string MapReturnType(string typeScriptType, TypeMapper mapper, out bool isAsync)
    {
        var trimmed = typeScriptType.Trim();
        if (TryUnwrapGeneric(trimmed, "Promise", out var inner) ||
            TryUnwrapGeneric(trimmed, "PromiseLike", out inner))
        {
            isAsync = true;
            var mapped = mapper.Map(inner);
            return mapped == "void" ? "Task" : "Task<" + mapped + ">";
        }

        isAsync = false;
        var result = mapper.Map(trimmed);
        return result == "void" ? "Task" : "Task<" + result + ">";
    }

    private static TypeMapper CreateMapper(
        SourceProductionContext context,
        DeclarationFile file,
        string namespaceName,
        Dictionary<string, Declaration> localTypes,
        Dictionary<string, Declaration> globalTypes)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);
        TypeMapper? mapper = null;

        string? Resolve(string name)
        {
            if (!localTypes.TryGetValue(name, out var declaration) &&
                !globalTypes.TryGetValue(name, out declaration))
            {
                return null;
            }

            return declaration.Kind switch
            {
                DeclarationKind.Api => "global::" + declaration.NamespaceName + "." + declaration.CSharpName,
                DeclarationKind.Dto => "global::" + declaration.NamespaceName + "." + declaration.CSharpName,
                DeclarationKind.Enum => "global::" + declaration.NamespaceName + "." + declaration.CSharpName,
                DeclarationKind.Alias => localTypes.ContainsKey(name) ? ResolveAlias(declaration) : null,
                _ => null,
            };
        }

        string? ResolveAlias(Declaration declaration)
        {
            if (aliases.TryGetValue(declaration.TypeScriptName, out var mapped))
            {
                return mapped;
            }

            if (declaration.AliasTarget is null || !resolving.Add(declaration.TypeScriptName))
            {
                return null;
            }

            try
            {
                mapped = mapper!.Map(declaration.AliasTarget);
                aliases[declaration.TypeScriptName] = mapped;
                return mapped;
            }
            finally
            {
                resolving.Remove(declaration.TypeScriptName);
            }
        }

        mapper = new TypeMapper(
            Resolve,
            fallback => context.ReportDiagnostic(Diagnostic.Create(TypeFallback, CreateLocation(file.Path, 0), fallback)));
        return mapper;
    }

    private static string NormalizeMemberSeparators(string body)
    {
        var builder = new StringBuilder(body.Length);
        var parentheses = 0;
        var braces = 0;
        var angles = 0;
        var brackets = 0;

        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (character == '\n' &&
                parentheses == 0 &&
                braces == 0 &&
                angles == 0 &&
                brackets == 0 &&
                StartsMember(body, index + 1))
            {
                builder.Append(';');
                continue;
            }

            builder.Append(character);
            switch (character)
            {
                case '(': parentheses++; break;
                case ')': parentheses = Math.Max(0, parentheses - 1); break;
                case '{': braces++; break;
                case '}': braces = Math.Max(0, braces - 1); break;
                case '<': angles++; break;
                case '>': angles = Math.Max(0, angles - 1); break;
                case '[': brackets++; break;
                case ']': brackets = Math.Max(0, brackets - 1); break;
            }
        }

        return builder.ToString();
    }

    private static bool StartsMember(string body, int index)
    {
        while (index < body.Length && char.IsWhiteSpace(body[index]))
        {
            index++;
        }

        if (index >= body.Length || !(char.IsLetter(body[index]) || body[index] is '_' or '$'))
        {
            return false;
        }

        while (index < body.Length && (char.IsLetterOrDigit(body[index]) || body[index] is '_' or '$'))
        {
            index++;
        }

        while (index < body.Length && char.IsWhiteSpace(body[index]))
        {
            index++;
        }

        return index < body.Length && (body[index] == '(' || body[index] == '?' || body[index] == ':');
    }

    private static IEnumerable<string> SplitMembers(string body)
    {
        var start = 0;
        var parentheses = 0;
        var braces = 0;
        var angles = 0;
        var brackets = 0;

        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            switch (character)
            {
                case '(': parentheses++; break;
                case ')': parentheses = Math.Max(0, parentheses - 1); break;
                case '{': braces++; break;
                case '}': braces = Math.Max(0, braces - 1); break;
                case '<': angles++; break;
                case '>': angles = Math.Max(0, angles - 1); break;
                case '[': brackets++; break;
                case ']': brackets = Math.Max(0, brackets - 1); break;
                case ';':
                    if (parentheses == 0 && braces == 0 && angles == 0 && brackets == 0)
                    {
                        yield return body.Substring(start, index - start);
                        start = index + 1;
                    }
                    break;
            }
        }

        if (start < body.Length)
        {
            yield return body.Substring(start);
        }
    }

    private static IEnumerable<string> SplitTopLevel(string value, char separator)
    {
        var start = 0;
        var parentheses = 0;
        var braces = 0;
        var angles = 0;
        var brackets = 0;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '(': parentheses++; break;
                case ')': parentheses = Math.Max(0, parentheses - 1); break;
                case '{': braces++; break;
                case '}': braces = Math.Max(0, braces - 1); break;
                case '<': angles++; break;
                case '>': angles = Math.Max(0, angles - 1); break;
                case '[': brackets++; break;
                case ']': brackets = Math.Max(0, brackets - 1); break;
                default:
                    if (character == separator && parentheses == 0 && braces == 0 && angles == 0 && brackets == 0)
                    {
                        yield return value.Substring(start, index - start);
                        start = index + 1;
                    }
                    break;
            }
        }

        yield return value.Substring(start);
    }

    private static int FindClosingBrace(string text, int openBrace)
    {
        var depth = 0;
        for (var index = openBrace; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                depth++;
            }
            else if (text[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static string? ParseNamespace(string text)
    {
        var match = NamespacePattern.Match(text);
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static string RemoveComments(string text)
    {
        text = Regex.Replace(text, @"/\*[\s\S]*?\*/", string.Empty);
        return Regex.Replace(text, @"//[^\r\n]*", string.Empty);
    }

    private static string ToInterfaceName(string name)
    {
        var identifier = Identifier(name);
        return identifier.StartsWith("I", StringComparison.Ordinal) ? identifier : "I" + identifier;
    }

    private static string ToPascal(string name)
    {
        var identifier = Identifier(name);
        if (identifier.StartsWith("@", StringComparison.Ordinal))
        {
            identifier = identifier.Substring(1);
        }

        identifier = char.ToUpperInvariant(identifier[0]) + identifier.Substring(1);
        return Keywords.Contains(identifier) ? "@" + identifier : identifier;
    }

    private static string Identifier(string name)
    {
        var cleaned = Regex.Replace(name, "[^A-Za-z0-9_]", "_");
        if (cleaned.Length == 0 || char.IsDigit(cleaned[0]))
        {
            cleaned = "_" + cleaned;
        }

        return Keywords.Contains(cleaned) ? "@" + cleaned : cleaned;
    }

    private static string MakeNullable(string type) => type.EndsWith("?", StringComparison.Ordinal) ? type : type + "?";

    private static string EscapeString(string value) => value.Replace(@"\", @"\\").Replace("\"", "\\\"");

    private static string Unquote(string value)
    {
        var content = value.Substring(1, value.Length - 2);
        return Regex.Replace(content, @"\\(.)", match => match.Groups[1].Value switch
        {
            "n" => "\n",
            "r" => "\r",
            "t" => "\t",
            _ => match.Groups[1].Value,
        });
    }

    private static string Sanitize(string value) => Regex.Replace(value, "[^A-Za-z0-9_]", "_");

    private static string Summarize(string value)
    {
        var collapsed = Regex.Replace(value, @"\s+", " ").Trim();
        return collapsed.Length <= 80 ? collapsed : collapsed.Substring(0, 77) + "...";
    }

    private static string FileName(string path)
    {
        var separator = path.LastIndexOfAny(['/', '\\']);
        var name = separator >= 0 ? path.Substring(separator + 1) : path;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }

    private static Location CreateLocation(string path, int position) =>
        Location.Create(path, new TextSpan(position, 0), new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));

    private static bool TryUnwrapGeneric(string type, string name, out string inner)
    {
        inner = string.Empty;
        if (!type.StartsWith(name, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = type.Substring(name.Length).Trim();
        if (rest.Length < 2 || rest[0] != '<' || rest[rest.Length - 1] != '>')
        {
            return false;
        }

        inner = rest.Substring(1, rest.Length - 2).Trim();
        return inner.Length > 0;
    }

    private static bool IsStringLiteral(string value) =>
        value.Length >= 2 &&
        ((value[0] == '"' && value[value.Length - 1] == '"') ||
         (value[0] == '\'' && value[value.Length - 1] == '\''));

    private static bool IsBooleanLiteral(string value) => value is "true" or "false";

    private static bool IsNumberLiteral(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private enum DeclarationKind
    {
        Api,
        Dto,
        Enum,
        Alias,
    }

    private sealed class DeclarationFile(string path, string text)
    {
        public string Path { get; } = path;

        public string Text { get; } = text;
    }

    private sealed class ParsedFile(
        DeclarationFile file,
        int index,
        string namespaceName,
        List<Declaration> declarations)
    {
        public DeclarationFile File { get; } = file;

        public int Index { get; } = index;

        public string NamespaceName { get; } = namespaceName;

        public List<Declaration> Declarations { get; } = declarations;
    }

    private sealed class Declaration(string typeScriptName, string namespaceName, Location location)
    {
        public string TypeScriptName { get; } = typeScriptName;

        public string NamespaceName { get; } = namespaceName;

        public Location Location { get; } = location;

        public DeclarationKind Kind { get; set; }

        public string CSharpName { get; set; } = typeScriptName;

        public List<string> BaseNames { get; } = [];

        public List<RawMethod> Methods { get; } = [];

        public List<RawProperty> Properties { get; } = [];

        public List<EnumMember> EnumMembers { get; } = [];

        public bool IsStringEnum { get; set; }

        public string? AliasTarget { get; set; }
    }

    private sealed class RawMethod(string name, string arguments, string returnType)
    {
        public string Name { get; } = name;

        public string Arguments { get; } = arguments;

        public string ReturnType { get; } = returnType;
    }

    private sealed class RawProperty(string name, string type, bool optional)
    {
        public string Name { get; } = name;

        public string Type { get; } = type;

        public bool Optional { get; } = optional;
    }

    private sealed class EnumMember(string name, string? stringValue, int? numericValue)
    {
        public string Name { get; } = name;

        public string? StringValue { get; } = stringValue;

        public int? NumericValue { get; } = numericValue;
    }

    private sealed class TypeMapper(Func<string, string?> resolve, Action<string> fallback)
    {
        public string Map(string typeScriptType)
        {
            var trimmed = typeScriptType.Trim();
            if (trimmed.Length == 0)
            {
                return "global::System.Text.Json.JsonElement";
            }

            var parts = SplitTopLevel(trimmed, '|').Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();
            var nullable = parts.Any(part => part is "null" or "undefined");
            var types = parts.Where(part => part is not "null" and not "undefined").ToArray();
            if (types.Length == 0)
            {
                return "object?";
            }

            string mapped;
            if (types.Length == 1)
            {
                mapped = MapSingle(types[0]);
            }
            else if (types.All(IsStringLiteral))
            {
                mapped = "string";
            }
            else if (types.All(IsBooleanLiteral))
            {
                mapped = "bool";
            }
            else if (types.All(IsNumberLiteral))
            {
                mapped = "double";
            }
            else
            {
                fallback(trimmed);
                mapped = "global::System.Text.Json.JsonElement";
            }

            return nullable ? MakeNullable(mapped) : mapped;
        }

        private string MapSingle(string type)
        {
            var trimmed = type.Trim();
            if (trimmed.EndsWith("?", StringComparison.Ordinal))
            {
                return MakeNullable(MapSingle(trimmed.Substring(0, trimmed.Length - 1)));
            }

            if (IsStringLiteral(trimmed))
            {
                return "string";
            }

            if (IsNumberLiteral(trimmed))
            {
                return "double";
            }

            if (IsBooleanLiteral(trimmed))
            {
                return "bool";
            }

            switch (trimmed)
            {
                case "string": return "string";
                case "number": return "double";
                case "boolean": return "bool";
                case "void": return "void";
                case "object": return "object";
                case "unknown":
                case "any": return "global::System.Text.Json.JsonElement";
                case "null":
                case "undefined": return "object?";
                case "Date": return "global::System.DateTimeOffset";
                case "ArrayBuffer":
                case "Uint8Array": return "byte[]";
            }

            if (TryUnwrapGeneric(trimmed, "ReadonlyArray", out var readonlyArray) ||
                TryUnwrapGeneric(trimmed, "ReadOnlyArray", out readonlyArray) ||
                TryUnwrapGeneric(trimmed, "Array", out readonlyArray))
            {
                return "global::System.Collections.Generic.IReadOnlyList<" + Map(readonlyArray) + ">";
            }

            if (TryUnwrapGeneric(trimmed, "Record", out var record))
            {
                var recordArguments = SplitTopLevel(record, ',').Select(argument => argument.Trim()).ToArray();
                if (recordArguments.Length == 2 && recordArguments[0] is "string" or "number")
                {
                    return "global::System.Collections.Generic.IReadOnlyDictionary<string, " + Map(recordArguments[1]) + ">";
                }

                fallback(trimmed);
                return "global::System.Text.Json.JsonElement";
            }

            if (trimmed.EndsWith("[]", StringComparison.Ordinal))
            {
                return "global::System.Collections.Generic.IReadOnlyList<" + Map(trimmed.Substring(0, trimmed.Length - 2)) + ">";
            }

            var resolved = resolve(trimmed);
            if (resolved is not null)
            {
                return resolved;
            }

            fallback(trimmed);
            return "global::System.Text.Json.JsonElement";
        }

        private static bool IsStringLiteral(string value) =>
            value.Length >= 2 &&
            ((value[0] == '"' && value[value.Length - 1] == '"') ||
             (value[0] == '\'' && value[value.Length - 1] == '\''));

        private static bool IsBooleanLiteral(string value) => value is "true" or "false";

        private static bool IsNumberLiteral(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }
}
