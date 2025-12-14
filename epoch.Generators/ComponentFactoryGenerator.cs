using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

[Generator]
public class ComponentFactoryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Find all structs with [Component]
        var components = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: (s, _) => s is StructDeclarationSyntax sd && sd.AttributeLists.Count > 0,
                transform: (ctx, _) => GetStructSymbol(ctx)
            )
            .Where(m => m is not null);

        var compilationAndComponents = context.CompilationProvider.Combine(components.Collect());

        // 2. Output the source
        context.RegisterSourceOutput(
            compilationAndComponents,
            static (spc, source) => Execute(source.Left, source.Right, spc)
        );
    }

    private static INamedTypeSymbol? GetStructSymbol(GeneratorSyntaxContext context)
    {
        var declaration = (StructDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(declaration);

        return
            symbol is INamedTypeSymbol namedSymbol
            && namedSymbol.GetAttributes().Any(a => a.AttributeClass?.Name == "ComponentAttribute")
            ? namedSymbol
            : null;
    }

    private static void Execute(
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol?> components,
        SourceProductionContext context
    )
    {
        if (components.IsDefaultOrEmpty)
            return;

        // 1. Setup the list of unique symbols
        var uniqueComponents = components
            .Where(c => c is not null)
            .Select(c => (INamedTypeSymbol)c!)
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>()
            .ToList();

        var sb = new StringBuilder();

        // Imports
        sb.AppendLine("using System;");
        sb.AppendLine("using Arch.Core;");
        sb.AppendLine("using Arch.Core.Utils;");
        sb.AppendLine("using epoch.ECS;");
        sb.AppendLine("using epoch.Utilities;");
        sb.AppendLine("using Microsoft.Xna.Framework;");

        sb.AppendLine("");
        sb.AppendLine("namespace epoch.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    public static class ComponentFactory");
        sb.AppendLine("    {");

        // --- METHOD 1: GetArchType ---
        // Generates: return Component<epoch.Components.Position>.ComponentType;
        sb.AppendLine("        public static ComponentType GetArchType(string typeName)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (typeName)");
        sb.AppendLine("            {");
        foreach (var comp in uniqueComponents)
        {
            sb.AppendLine($"                case \"{comp.Name}\":");
            sb.AppendLine(
                $"                    return Component<{comp.ToDisplayString()}>.ComponentType;"
            );
        }
        sb.AppendLine("                default:");
        sb.AppendLine(
            "                    throw new ArgumentException($\"Unknown component: {typeName}\");"
        );
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("");

        // --- METHOD 2: SetOnEntity ---
        // Generates: world.Set<T>(entity, component);
        sb.AppendLine(
            "        public static void SetOnEntity(this Entity entity, World world, ComponentDefinition def)"
        );
        sb.AppendLine("        {");
        sb.AppendLine("            switch (def.TypeName)");
        sb.AppendLine("            {");
        foreach (var comp in uniqueComponents)
        {
            GenerateSetCase(sb, comp);
        }
        sb.AppendLine("                default:");
        sb.AppendLine(
            "                    throw new ArgumentException($\"Unknown component: {def.TypeName}\");"
        );
        sb.AppendLine("            }");
        sb.AppendLine("        }");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("ComponentFactory.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    // Helper method to write the specific property parsing logic
    private static void GenerateSetCase(StringBuilder sb, INamedTypeSymbol comp)
    {
        sb.AppendLine($"                case \"{comp.Name}\":");
        sb.AppendLine("                {");

        // 1. Create the struct
        sb.AppendLine($"                    var component = new {comp.ToDisplayString()}();");

        // 2. Scan public properties/fields to generate assignments
        var members = comp.GetMembers()
            .Where(m =>
                (
                    m is IFieldSymbol f
                    && f.IsReadOnly == false
                    && f.DeclaredAccessibility == Accessibility.Public
                )
                || (
                    m is IPropertySymbol p
                    && p.SetMethod is not null
                    && p.DeclaredAccessibility == Accessibility.Public
                )
            );

        foreach (var member in members)
        {
            var type = member is IFieldSymbol f ? f.Type : ((IPropertySymbol)member).Type;
            string parseFunc = GetParseMethodName(type);

            // Generates: if (def.TryGet("TileId", out var val_TileId))
            sb.AppendLine(
                $"                    if (def.TryGet(\"{member.Name}\", out var val_{member.Name}))"
            );
            // Generates:     component.TileId = ComponentParsers.ParseInt(val_TileId);
            sb.AppendLine(
                $"                        component.{member.Name} = ComponentParsers.{parseFunc}(val_{member.Name});"
            );
        }

        // 3. Use world.Set
        sb.AppendLine(
            $"                    world.Set<{comp.ToDisplayString()}>(entity, component);"
        );
        sb.AppendLine("                    break;");
        sb.AppendLine("                }");
    }

    // Helper to map types to your parser methods
    private static string GetParseMethodName(ITypeSymbol type)
    {
        if (
            type is INamedTypeSymbol namedType
            && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
        )
        {
            type = namedType.TypeArguments[0];
        }

        return type.Name switch
        {
            "Int32" => "ParseInt",
            "Single" => "ParseFloat",
            "Boolean" => "ParseBool",
            "String" => "ParseString",
            "Vector2" => "ParseVector2",
            "Vector3" => "ParseVector3",
            "Color" => "ParseColor",
            _ => "ParseString",
        };
    }
}
