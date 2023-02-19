﻿using Microsoft.CodeAnalysis;
using System.Linq;

namespace DecoratorGenerator
{
    [Generator]
    public class Main : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            var types = context.Compilation.Assembly.GlobalNamespace
                .GetNamespaceMembers()
                .SelectMany(n => n.GetTypeMembers())
                .Where(t => t.GetAttributes()
                             .Where(@as => @as.AttributeClass.Name == "DecorateAttribute")
                             .Any());


            var thirdPartyTypes = context.Compilation.Assembly.GetTypeByMetadataName("WrapperList").GetMembers()
                .Where(m => m.Name != ".ctor")
                .Select(m => m as IFieldSymbol)
                .Select(f => f.Type)
                .Select(t => t as INamedTypeSymbol);

            types = types.Concat(thirdPartyTypes);

            var outputs = types.Select(type => {
                var className = $"{type.Name.Substring(1)}Decorator";
                var @interface = type;
                var targetFieldName = $"{char.ToLowerInvariant(@interface.Name[1])}{@interface.Name.Substring(2)}";
                var members = @interface.GetMembers();
                var methods = members.Where(member => member is IMethodSymbol && !((member as IMethodSymbol).AssociatedSymbol is IPropertySymbol)).Select(m => m as IMethodSymbol);
                var properties = members.Where(member => member is IPropertySymbol).Select(m => m as IPropertySymbol);
                var displayMethods = methods.Select(method => {
                    var typeParametersStrings = method.TypeParameters.Select(t => t.ToDisplayString());
                    var parametersStrings = method.Parameters.Select(p => $@"{p.Type} {p.Name}");
                    var formattedAccessibility = $@"{char.ToLowerInvariant(method.ReturnType.DeclaredAccessibility.ToString()[0])}{method.ReturnType.DeclaredAccessibility.ToString().Substring(1)}";
                    var signature = $@"{formattedAccessibility} virtual {method.ReturnType} {method.Name}{(method.IsGenericMethod ? $@"<{string.Join(", ", typeParametersStrings)}>" : string.Empty)}({string.Join(", ", parametersStrings)})";
                    var callParameters = $@"{string.Join(", ", method.Parameters.Select(p => p.Name))}";

                    var call = $@"{targetFieldName}.{method.Name}({callParameters})";

                    return (signature, call, returnType: method.ReturnType);
                });
                var displayProperties = properties.Select(property => {
                    var formattedAccessibility = $@"{char.ToLowerInvariant(property.Type.DeclaredAccessibility.ToString()[0])}{property.Type.DeclaredAccessibility.ToString().Substring(1)}";
                    var signature = $@"{formattedAccessibility} virtual {property.Type} {property.Name}";
                    var call = $@"get => cat.{property.Name}; set => {targetFieldName}.{property.Name} = value;";

                    return (signature, call, string.Empty);
                });

                var formattedDisplayMethods = displayMethods.Select(method => {
                    return
$@"    {method.signature} {{
        {(method.returnType.Name == "Void" ? string.Empty : "return")} {method.call};
    }}";
                });

                var formattedDisplayProperties = displayProperties.Select(property => {
                    return
$@"    {property.signature} {{ {property.call} }}";
                });

                var source =
$@"// <auto-generated/>
namespace {type.ContainingNamespace.ToDisplayString()};

public abstract class {className} : {@interface.Name}
{{
    private {@interface.Name} {targetFieldName};

    protected {className} ({@interface.Name} {targetFieldName}) {{
        this.{targetFieldName} = {targetFieldName};
    }}

{string.Join("\n\n", formattedDisplayProperties)}

{string.Join("\n\n", formattedDisplayMethods)}
}}
";

                return (source, className);
            });

            foreach (var (source, className) in outputs) {
                // Add the source code to the compilation
                context.AddSource($"{className}.generated.cs", source);
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            // No initialization required for this one
        }
    }
}