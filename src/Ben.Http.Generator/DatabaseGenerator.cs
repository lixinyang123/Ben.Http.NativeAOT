using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

[Generator]
public class DatabaseGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not DatabaseReceiver receiver ||
            (receiver.QueryAsyncInvocations is null &&
             receiver.QueryRowAsyncInvocations is null))
        {
            return;
        }

        StringBuilder sb = new();
        _ = sb.AppendLine($@"// Source Generated at {DateTimeOffset.Now:R}
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Ben.Http
{{
    public static class DatabaseExtensionsGenerated
    {{");

        if (receiver.QueryAsyncInvocations is not null)
        {
            OutputQueryAsync(context.Compilation, receiver.QueryAsyncInvocations, sb);
        }
        if (receiver.QueryRowAsyncInvocations is not null)
        {
            OutputQueryRowAsync(context.Compilation, receiver.QueryRowAsyncInvocations, sb);
        }

        _ = sb.AppendLine(@"
    }
}");
        context.AddSource("Database", sb.ToString());
    }

    private void OutputQueryRowAsync(Compilation compilation, List<InvocationExpressionSyntax> queryAsyncInvocations, StringBuilder sb)
    {
        HashSet<(string dbType, string arg0, string arg1, string methodName)> invocations = new();
        foreach (InvocationExpressionSyntax invocation in queryAsyncInvocations)
        {
            SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count != 2)
            {
                continue;
            }

            ExpressionSyntax argument = arguments[0].Expression;
            if (argument.Kind() != SyntaxKind.StringLiteralExpression)
            {
                continue;
            }
            argument = arguments[1].Expression;

            ExpressionSyntax expression = invocation.Expression;
            if (expression is MemberAccessExpressionSyntax member)
            {
                if (member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                {
                    SemanticModel semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
                    ITypeSymbol? type = null;
                    foreach (SyntaxNode child in expression.ChildNodes())
                    {
                        if (child is IdentifierNameSyntax identifier)
                        {
                            type = semanticModel.GetTypeInfo(identifier).Type;
                        }
                        else if (child is GenericNameSyntax methodIdent)
                        {
                            TypeArgumentListSyntax typeArgs = methodIdent.TypeArgumentList;
                            if (type is not null && typeArgs.Arguments.Count == 2)
                            {
                                string dbType = type.ToString();
                                TypeSyntax arg0 = typeArgs.Arguments[0];
                                TypeSyntax arg1 = typeArgs.Arguments[1];
                                string argStr0 = arg0.ToString();
                                string argStr1 = arg1.ToString();
                                string methodName = SanitizeIdentifier(argStr0 + argStr1);

                                if (!invocations.Contains((dbType, argStr0, argStr1, methodName)))
                                {
                                    GenerateQueryRowMethod(semanticModel, dbType, methodName, arg0, arg1, sb);
                                    _ = invocations.Add((dbType, argStr0, argStr1, methodName));
                                }
                            }
                            break;
                        }
                    }
                }
            }
        }

        foreach (IGrouping<string, (string dbType, string arg0, string arg1, string methodName)>? db in invocations.GroupBy(type => type.dbType))
        {
            _ = sb.AppendLine(@$"
        public static Task<TResult> QueryRowAsync<TResult, TValue>(this {db.Key} conn, string sql, (string name, TValue value) parameter, bool autoClose = true)
        {{");
            foreach ((string dbType, string arg0, string arg1, string methodName) in db)
            {
                _ = sb.AppendLine(@$"
            if (typeof(TResult) == typeof({arg0}) && typeof(TValue) == typeof({arg1}))
            {{
                return (Task<TResult>)(object)QueryRowAsync_{methodName}(conn, sql, (parameter.name, ({arg1})(object)parameter.value!), autoClose);
            }}");

            }
            _ = sb.AppendLine(@$"
            throw new NotImplementedException();
        }}");
        }
    }

    private void GenerateQueryRowMethod(SemanticModel semanticModel, string dbType, string methodName, TypeSyntax argResult, TypeSyntax argParam, StringBuilder sb)
    {
        string dbPrefix = dbType.Replace("Connection", "");
        _ = sb.AppendLine(@$"
        private static async Task<{argResult}> QueryRowAsync_{methodName}({dbType} conn, string sql, (string name, {argParam} value) param, bool autoClose)
        {{
            using var cmd = new {dbPrefix}Command(sql, conn);
            var parameter = new {dbPrefix}Parameter<{argParam}>(parameterName: param.name, value: param.value);
            cmd.Parameters.Add(parameter);
            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();");

        ITypeSymbol? type = semanticModel.GetTypeInfo(argResult).Type;

        IFieldSymbol[] fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsImplicitlyDeclared).ToArray();
        IPropertySymbol[] properties = type.GetMembers().OfType<IPropertySymbol>().Where(f => !f.IsReadOnly).ToArray();

        _ = sb.Append(@$"
            if (!autoClose)
            {{
                return new ()
                {{");
        foreach (IFieldSymbol? field in fields)
        {
            _ = sb.Append(@$"
                    {field.Name} = reader.Get{GetType(field.Type.SpecialType)}(""{field.Name}""),");
        }
        foreach (IPropertySymbol? property in properties)
        {
            _ = sb.Append(@$"
                    {property.Name} = reader.Get{GetType(property.Type.SpecialType)}(""{property.Name}""),");
        }

        _ = sb.Remove(sb.Length - 1, 1);

        _ = sb.Append(@$"
                }};
            }}
            else
            {{
                var retVal = new {argResult}()
                {{");
        foreach (IFieldSymbol? field in fields)
        {
            _ = sb.Append(@$"
                    {field.Name} = reader.Get{GetType(field.Type.SpecialType)}(""{field.Name}""),");
        }
        foreach (IPropertySymbol? property in properties)
        {
            _ = sb.Append(@$"
                    {property.Name} = reader.Get{GetType(property.Type.SpecialType)}(""{property.Name}""),");
        }

        _ = sb.Remove(sb.Length - 1, 1);

        _ = sb.AppendLine(@$"
                }};
            
                conn.Close();

                return retVal;
            }}
    
        }}");
    }


    private void OutputQueryAsync(Compilation compilation, List<InvocationExpressionSyntax> queryAsyncInvocations, StringBuilder sb)
    {
        HashSet<(string dbType, string arg, string methodName)> invocations = new();
        foreach (InvocationExpressionSyntax invocation in queryAsyncInvocations)
        {
            SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count != 1)
            {
                continue;
            }

            ExpressionSyntax argument = arguments[0].Expression;
            if (argument.Kind() != SyntaxKind.StringLiteralExpression)
            {
                continue;
            }

            ExpressionSyntax expression = invocation.Expression;
            if (expression is MemberAccessExpressionSyntax member)
            {
                if (member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                {
                    SemanticModel semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
                    ITypeSymbol? type = null;
                    foreach (SyntaxNode child in expression.ChildNodes())
                    {
                        if (child is IdentifierNameSyntax identifier)
                        {
                            type = semanticModel.GetTypeInfo(identifier).Type;
                        }
                        else if (child is GenericNameSyntax methodIdent)
                        {
                            TypeArgumentListSyntax typeArgs = methodIdent.TypeArgumentList;
                            if (type is not null && typeArgs.Arguments.Count == 1)
                            {
                                string dbType = type.ToString();
                                TypeSyntax arg = typeArgs.Arguments[0];
                                string argStr = arg.ToString();
                                string methodName = SanitizeIdentifier(argStr);

                                if (!invocations.Contains((dbType, argStr, methodName)))
                                {
                                    GenerateQueryMethod(semanticModel, dbType, methodName, arg, sb);
                                    _ = invocations.Add((dbType, argStr, methodName));
                                }
                            }
                            break;
                        }
                    }
                }
            }
        }

        foreach (IGrouping<string, (string dbType, string arg, string methodName)>? db in invocations.GroupBy(type => type.dbType))
        {
            _ = sb.AppendLine(@$"
        public static Task<List<TResult>> QueryAsync<TResult>(this {db.Key} conn, string sql, bool autoClose = true)
        {{");
            foreach ((string dbType, string arg, string methodName) in db)
            {
                _ = sb.AppendLine(@$"
            if (typeof(TResult) == typeof({arg}))
            {{
                return (Task<List<TResult>>)(object)QueryAsync_{methodName}(conn, sql, autoClose);
            }}");

            }
            _ = sb.AppendLine(@$"
            throw new NotImplementedException();
        }}");
        }
    }

    private void GenerateQueryMethod(SemanticModel semanticModel, string dbType, string methodName, TypeSyntax arg, StringBuilder sb)
    {
        _ = sb.AppendLine(@$"
        private static async Task<List<{arg}>> QueryAsync_{methodName}({dbType} conn, string sql, bool autoClose)
        {{
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var list = new List<{arg}>(16);
            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();");

        ITypeSymbol? type = semanticModel.GetTypeInfo(arg).Type;

        int count = 0;
        IFieldSymbol[] fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsImplicitlyDeclared).ToArray();
        IPropertySymbol[] properties = type.GetMembers().OfType<IPropertySymbol>().Where(f => !f.IsReadOnly).ToArray();

        foreach (IFieldSymbol? field in fields)
        {
            _ = sb.Append(@$"
            var f{count} = reader.GetOrdinal(""{field.Name}"");");

            count++;
        }
        foreach (IPropertySymbol? propery in properties)
        {
            _ = sb.Append(@$"
            var f{count} = reader.GetOrdinal(""{propery.Name}"");");

            count++;
        }
        count = 0;

        _ = sb.Append(@$"
            while (await reader.ReadAsync())
            {{
                list.Add(new ()
                {{");
        foreach (IFieldSymbol? field in fields)
        {
            _ = sb.Append(@$"
                    {field.Name} = reader.Get{GetType(field.Type.SpecialType)}(f{count}),");

            count++;
        }
        foreach (IPropertySymbol? property in properties)
        {
            _ = sb.Append(@$"
                    {property.Name} = reader.Get{GetType(property.Type.SpecialType)}(f{count}),");

            count++;
        }

        _ = sb.Remove(sb.Length - 1, 1);

        _ = sb.AppendLine(@$"
                }});
            }}

            if (autoClose) conn.Close();

            return list;      
        }}");
    }

    private string GetType(SpecialType specialType)
    {
        string type = specialType.ToString();
        return type.Replace("System_", "");
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
        context.RegisterForSyntaxNotifications(() => new DatabaseReceiver());
    }

    private class DatabaseReceiver : ISyntaxReceiver
    {
        public List<InvocationExpressionSyntax>? QueryAsyncInvocations { get; private set; }
        public List<InvocationExpressionSyntax>? QueryRowAsyncInvocations { get; private set; }

        public void OnVisitSyntaxNode(SyntaxNode node)
        {
            if (node.IsKind(SyntaxKind.InvocationExpression) &&
                node is InvocationExpressionSyntax invocation)
            {
                ExpressionSyntax expression = invocation.Expression;
                if (expression is MemberAccessExpressionSyntax member)
                {
                    if (member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        foreach (SyntaxNode child in expression.ChildNodes())
                        {
                            if (child is GenericNameSyntax methodIdent)
                            {
                                string valueText = methodIdent.Identifier.ValueText;
                                switch (valueText)
                                {
                                    case "QueryAsync":
                                        (QueryAsyncInvocations ??= new()).Add(invocation);
                                        break;
                                    case "QueryRowAsync":
                                        (QueryRowAsyncInvocations ??= new()).Add(invocation);
                                        break;
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
