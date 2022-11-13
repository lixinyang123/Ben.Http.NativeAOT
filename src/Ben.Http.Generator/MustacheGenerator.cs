using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

[Generator]
public class MustacheGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not MustacheRenderReceiver receiver)
        {
            return;
        }

        StringBuilder sb = new();
        _ = sb.AppendLine($@"// Source Generated at {DateTimeOffset.Now:R}
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ben.Http.Templates;

namespace Ben.Http
{{
    public class MustacheTemplates
    {{
        // When passing a `new byte[] {{ ... }}` to a `ReadOnlySpan<byte>` receiver,
        // the C# compiler will emit a load of the data section rather than constructing a new array.
        // We make use of that here.");

        foreach (AdditionalText file in context.AdditionalFiles)
        {
            bool isMustacheTemplate = string.Equals(
                context.AnalyzerConfigOptions.GetOptions(file).TryGetAdditionalFileMetadataValue("IsMustacheTemplate"),
                "true",
                StringComparison.OrdinalIgnoreCase
            );

            if (!isMustacheTemplate)
            {
                continue;
            }

            string? content = file.GetText(context.CancellationToken)?.ToString();

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            ProcessFile(context, file.Path, content!, receiver, sb);

        }

        _ = sb.AppendLine(@"
    }
}");
        context.AddSource("MustacheTemplates", sb.ToString());
    }

    private const int SpacesPerIndent = 4;
    private void ProcessFile(in GeneratorExecutionContext context, string filePath, string content, MustacheRenderReceiver? receiver, StringBuilder builder)
    {
        // Generate class name from file name
        string templateName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(filePath));

        // Always output non-specific writer
        _ = builder.AppendLine(@$"
        public static void Render{templateName}<T>(T model, IBufferWriter<byte> writer)
        {{
            // Emitted as an initial call site for the template,
            // when actually called a specific call site for the exact model will be additionally be emitted.
            throw new NotImplementedException();        
        }}
");

        if (receiver?.Invocations?.TryGetValue(templateName, out List<InvocationExpressionSyntax>? invocations) ?? false)
        {
            Debug.Assert(invocations != null);

            foreach (InvocationExpressionSyntax invocation in invocations!)
            {
                SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
                if (arguments.Count != 2)
                {
                    continue;
                }

                SemanticModel semanticModel = context.Compilation.GetSemanticModel(invocation.SyntaxTree);
                ITypeSymbol? modelType = semanticModel.GetTypeInfo(arguments[0].Expression).Type;

                string indentSpaces = new(' ', SpacesPerIndent * 2);

                _ = builder.AppendLine(@$"{indentSpaces}public static void Render{templateName}({modelType} model, PipeWriter writer)");
                _ = builder.AppendLine(@$"{indentSpaces}{{");

                indentSpaces = IncreasedIndent(indentSpaces);

                if (modelType?.ToString().StartsWith("System.Collections.Generic.List<") ?? false)
                {
                    _ = builder.AppendLine(@$"{indentSpaces}var input = CollectionsMarshal.AsSpan(model);");
                }
                else
                {
                    _ = builder.AppendLine(@$"{indentSpaces}var input = model;");
                }
                _ = builder.AppendLine(@$"{indentSpaces}var output = new BufferWriter<PipeWriter>(writer, sizeHint: 1600);");

                ReadOnlySpan<char> remaining = RenderSubSection(content.AsSpan(), "input", modelType, default, indentSpaces, builder);

                _ = builder.Append(@$"{indentSpaces}/*{Environment.NewLine}{indentSpaces}{remaining.ToString().Replace("\n", $"\n{indentSpaces}")}{Environment.NewLine}{indentSpaces}*/{Environment.NewLine}");
                _ = builder.Append(@$"{indentSpaces}output.Write(");
                AddUtf8ByteArray(remaining, IncreasedIndent(indentSpaces), builder);
                _ = builder.AppendLine(");");

                _ = builder.AppendLine(@$"{indentSpaces}output.Commit();
        }}");
            }
        }
    }

    private static ReadOnlySpan<char> RenderSubSection(ReadOnlySpan<char> input, string name, ITypeSymbol type, ReadOnlySpan<char> sectionTag, string indentSpaces, StringBuilder builder)
    {
        for (int index = input.IndexOf("{{".AsSpan(), StringComparison.Ordinal); index >= 0; index = input.IndexOf("{{".AsSpan(), StringComparison.Ordinal))
        {
            ReadOnlySpan<char> tag = input.Slice(index + 2);
            int end = tag.IndexOf("}}".AsSpan());
            if (end <= 0)
            {
                break;
            }
            tag = tag.Slice(0, end);

            if (index > 0)
            {
                ReadOnlySpan<char> html = input.Slice(0, index);
                _ = builder.Append(@$"{indentSpaces}/*{Environment.NewLine}{indentSpaces}{html.ToString().Replace("\n", $"\n{indentSpaces}")}{Environment.NewLine}{indentSpaces}*/{Environment.NewLine}");

                _ = builder.Append(@$"{indentSpaces}output.Write(");
                AddUtf8ByteArray(html, IncreasedIndent(indentSpaces), builder);
                _ = builder.AppendLine(");");
            }

            input = input.Slice(index + end + 4);
            if (tag[0] == '#')
            {
                tag = tag.Slice(1);
                ITypeSymbol? subType = type;
                if (tag.Length == 1 && tag[0] == '.')
                {
                    tag = name.AsSpan();
                }
                else
                {
                    string tagName = tag.ToString();
                    ITypeSymbol? symbol = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.Name == tagName).FirstOrDefault()?.Type;
                    symbol ??= type.GetMembers().OfType<IPropertySymbol>().Where(p => (p.GetMethod?.Name) == "get_" + tagName).FirstOrDefault()?.Type;

                    subType = symbol;

                    if (subType is null)
                    {
                        // Skip section
                        string sectionEndTag = "{{/" + tag.ToString() + "}}";
                        int sectionEnd = input.IndexOf(sectionEndTag.AsSpan(), StringComparison.Ordinal);
                        if (end >= 0)
                        {
                            input = input.Slice(sectionEnd + sectionEndTag.Length);
                        }
                        continue;
                    }
                }

                bool isEnumerable = false;
                if (subType.SpecialType == SpecialType.None)
                {
                    foreach (INamedTypeSymbol iface in subType.AllInterfaces)
                    {
                        if (iface.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                        {
                            isEnumerable = true;
                            subType = iface.TypeArguments[0];
                            break;
                        }
                    }
                }

                // section start
                _ = builder.AppendLine($"{indentSpaces}// Start Section: {tag.ToString()}");

                if (isEnumerable)
                {
                    _ = builder.AppendLine($"{indentSpaces}foreach (var item in {tag.ToString()})");
                    _ = builder.AppendLine($"{indentSpaces}{{");

                    input = RemoveTrailingSpace(input);

                    input = RenderSubSection(input, "item", subType, tag, IncreasedIndent(indentSpaces), builder);

                    _ = builder.AppendLine($"{indentSpaces}}}");
                }
                else if (subType.SpecialType == SpecialType.System_Boolean)
                {
                    _ = builder.AppendLine($"{indentSpaces}if ({name}.{tag.ToString()})");
                    _ = builder.AppendLine($"{indentSpaces}{{");

                    input = RemoveTrailingSpace(input);

                    input = RenderSubSection(input, name, type, tag, IncreasedIndent(indentSpaces), builder);

                    _ = builder.AppendLine($"{indentSpaces}}}");
                }

                _ = builder.AppendLine($"{indentSpaces}// End Section: {tag.ToString()}");
            }
            else if (tag[0] == '/')
            {
                tag = tag.Slice(1);
                // section end
                if (tag.SequenceEqual(sectionTag))
                {
                    input = RemoveTrailingSpace(input);
                    break;
                }
            }
            else
            {
                string tagName = tag.ToString();

                ITypeSymbol? symbol = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.Name == tagName).FirstOrDefault()?.Type;
                symbol ??= type.GetMembers().OfType<IPropertySymbol>().Where(p => (p.GetMethod?.Name) == "get_" + tagName).FirstOrDefault()?.Type;

                _ = builder.AppendLine($"{indentSpaces}// Variable: {symbol} {name}.{tag.ToString()}");
                if (symbol is null)
                {
                    // Skip output
                }
                else if (symbol.SpecialType == SpecialType.System_Int32)
                {
                    _ = builder.AppendLine($"{indentSpaces}output.WriteNumeric((uint){name}.{tag.ToString()});");
                }
                else if (symbol.SpecialType == SpecialType.System_String)
                {
                    _ = builder.AppendLine($"{indentSpaces}output.WriteUtf8HtmlString({name}.{tag.ToString()});");
                }
                else
                {
                    _ = builder.AppendLine($"{indentSpaces}output.WriteUtf8HtmlString({name}.{tag.ToString()}.ToString());");
                }
            }
        }

        return input;
    }

    private static ReadOnlySpan<char> RemoveTrailingSpace(ReadOnlySpan<char> input)
    {
        int offset;
        for (offset = 0; offset < input.Length; offset++)
        {
            char ch = input[offset];
            if (ch is not '\n' and not '\r' and not ' ')
            {
                break;
            }
        }
        if (offset > 0)
        {
            input = input.Slice(offset);
        }

        return input;
    }

    private static string IncreasedIndent(string indentSpaces)
    {
        return indentSpaces + new string(' ', SpacesPerIndent);
    }

    private static void AddUtf8ByteArray(ReadOnlySpan<char> rawText, string indentSpaces, StringBuilder builder)
    {
        const int SpaceAt = 4;
        const int AdditionalSpaceAt = 8;
        const int WrapLineAt = 16;

        _ = builder.Append(@"new byte[] {");

        if (rawText.Length > 0)
        {
            int output = 0;
            // Keep inline if only one line
            bool isInline = rawText.Length <= WrapLineAt;
            foreach (byte b in Encoding.UTF8.GetBytes(rawText.ToString()))
            {
                if (!isInline)
                {
                    if (output % WrapLineAt == 0)
                    {
                        _ = builder.AppendLine();
                        _ = builder.Append(indentSpaces);
                    }
                }

                if (output % WrapLineAt != 0)
                {
                    if (output % SpaceAt == 0)
                    {
                        _ = builder.Append(' ');
                    }
                    if (output % AdditionalSpaceAt == 0)
                    {
                        _ = builder.Append(' ');
                    }
                }

                _ = builder.Append($"0x{b:x2},");
                output++;
            }
            // Replace the trailing comma; it is still valid C# without removing it
            // so its just about being tidy.
            if (isInline)
            {
                _ = builder.Replace(',', '}', builder.Length - 1, 1);
            }
            else
            {
                _ = builder.Replace(",", Environment.NewLine + indentSpaces.Substring(0, indentSpaces.Length - SpacesPerIndent) + "}", builder.Length - 1, 1);
            }
        }
        else
        {
            _ = builder.Append("}");
        }
    }

    private static string SanitizeIdentifier(string symbolName)
    {
        if (string.IsNullOrWhiteSpace(symbolName))
        {
            return string.Empty;
        }

        StringBuilder sb = new(symbolName.Length);
        if (!char.IsLetter(symbolName[0]))
        {
            // Must start with a letter or an underscore
            _ = sb.Append('_');
        }

        bool capitalize = true;
        foreach (char ch in symbolName)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                capitalize = true;
                continue;
            }

            _ = sb.Append(capitalize ? char.ToUpper(ch) : ch);
            capitalize = false;
        }

        return sb.ToString();
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new MustacheRenderReceiver());
    }

    private class MustacheRenderReceiver : ISyntaxReceiver
    {
        public Dictionary<string, List<InvocationExpressionSyntax>>? Invocations { get; private set; }

        public void OnVisitSyntaxNode(SyntaxNode node)
        {
            if (node.IsKind(SyntaxKind.InvocationExpression) &&
                node is InvocationExpressionSyntax invocation)
            {
                ExpressionSyntax expression = invocation.Expression;
                if (expression is MemberAccessExpressionSyntax member)
                {
                    bool isMustache = false;
                    string? template = null;
                    if (member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        foreach (SyntaxNode child in expression.ChildNodes())
                        {
                            if (!isMustache)
                            {
                                if (child is IdentifierNameSyntax classIdent)
                                {
                                    string valueText = classIdent.Identifier.ValueText;
                                    Console.Error.WriteLine(valueText);
                                    if (classIdent.Identifier.ValueText == "MustacheTemplates")
                                    {
                                        isMustache = true;
                                        continue;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }

                            if (child is IdentifierNameSyntax methodIdent)
                            {
                                string valueText = methodIdent.Identifier.ValueText;
                                if (valueText.IndexOf("Render", StringComparison.Ordinal) == 0)
                                {
                                    template = valueText.Substring("Render".Length);
                                }
                                break;
                            }
                        }

                        if (isMustache && template is not null)
                        {
                            if ((Invocations ??= new()).TryGetValue(template, out List<InvocationExpressionSyntax>? list))
                            {
                                list.Add(invocation);
                            }
                            else
                            {
                                Invocations.Add(template, new() { invocation });
                            }
                        }
                    }
                }
            }
        }
    }
}

internal static class SourceGeneratorExtensions
{
    public static string? TryGetValue(this AnalyzerConfigOptions options, string key)
    {
        return options.TryGetValue(key, out string? value) ? value : null;
    }

    public static string? TryGetAdditionalFileMetadataValue(this AnalyzerConfigOptions options, string propertyName)
    {
        return options.TryGetValue($"build_metadata.AdditionalFiles.{propertyName}");
    }
}