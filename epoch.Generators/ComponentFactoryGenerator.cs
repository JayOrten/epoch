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
        var components = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: (s, _) => s is StructDeclarationSyntax sd && sd.AttributeLists.Count > 0,
                transform: (ctx, _) => GetStructSymbol(ctx)
            )
            .Where(m => m is not null);

        var compilationAndComponents = context.CompilationProvider.Combine(components.Collect());

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

        var uniqueComponents = components
            .Where(c => c is not null)
            .Select(c => (INamedTypeSymbol)c!)
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>()
            .ToList();

        var sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;"); // Needed for Unsafe
        sb.AppendLine("using Arch.Core;");
        sb.AppendLine("using Arch.Core.Utils;");
        sb.AppendLine("using epoch.ECS;");
        sb.AppendLine("using epoch.Utilities;");
        sb.AppendLine("using Microsoft.Xna.Framework;");

        sb.AppendLine("");
        sb.AppendLine("namespace epoch.ECS");
        sb.AppendLine("{");
        sb.AppendLine("    public static partial class ComponentFactory");
        sb.AppendLine("    {");

        // Hooks
        sb.AppendLine(
            "        static partial void TrySetCustom(Entity entity, World world, ComponentDefinition def, ref bool handled);"
        );
        sb.AppendLine(
            "        static partial void TryCreateCustom(ComponentDefinition def, ref object component, ref bool handled);"
        );
        sb.AppendLine("");

        // --- METHOD 1: GetArchType ---
        sb.AppendLine("        public static ComponentType GetArchType(string typeName)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (typeName)");
        sb.AppendLine("            {");
        foreach (var comp in uniqueComponents)
        {
            sb.AppendLine(
                $"                case \"{comp.Name}\": return Component<{comp.ToDisplayString()}>.ComponentType;"
            );
        }
        sb.AppendLine(
            "                default: throw new ArgumentException($\"Unknown component: {typeName}\");"
        );
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("");

        // --- METHOD 2: Generic Create<T> (Zero Boxing) ---
        // This is the specific version you call when you know the type: var t = Factory.Create<GraphicalTile>(def);
        sb.AppendLine(
            "        public static T Create<T>(ComponentDefinition def) where T : struct"
        );
        sb.AppendLine("        {");

        // Generate if-checks for T. Since T is known at JIT time, the JIT removes the dead branches.
        // This is extremely fast (essentially a direct method call).
        foreach (var comp in uniqueComponents)
        {
            if (ShouldUseCustomFactory(comp))
                continue;

            sb.AppendLine($"            if (typeof(T) == typeof({comp.ToDisplayString()}))");
            sb.AppendLine("            {");
            sb.AppendLine($"                var val = new {comp.ToDisplayString()}();");
            GenerateParsingLogic(sb, comp, "val"); // Helper method to keep things clean
            // Unsafe.As casts the reference without boxing.
            sb.AppendLine(
                "                return Unsafe.As<" + comp.ToDisplayString() + ", T>(ref val);"
            );
            sb.AppendLine("            }");
        }

        sb.AppendLine(
            "            throw new ArgumentException($\"Unknown or non-component type: {typeof(T).Name}\");"
        );
        sb.AppendLine("        }");
        sb.AppendLine("");

        // --- METHOD 3: CreateComponent (Boxed Fallback) ---
        // Used when you only have the string name at runtime.
        sb.AppendLine("        public static object CreateComponent(ComponentDefinition def)");
        sb.AppendLine("        {");
        sb.AppendLine("            object customComponent = null;");
        sb.AppendLine("            bool handled = false;");
        sb.AppendLine("            TryCreateCustom(def, ref customComponent, ref handled);");
        sb.AppendLine("            if (handled) return customComponent;");
        sb.AppendLine("");
        sb.AppendLine("            switch (def.TypeName)");
        sb.AppendLine("            {");
        foreach (var comp in uniqueComponents)
        {
            // We just call the generic version and box the result.
            // This prevents duplicating the parsing logic in two places.
            sb.AppendLine(
                $"                case \"{comp.Name}\": return Create<{comp.ToDisplayString()}>(def);"
            );
        }
        sb.AppendLine(
            "                default: throw new ArgumentException($\"Unknown component: {def.TypeName}\");"
        );
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("");

        // --- METHOD 4: SetOnEntity ---
        sb.AppendLine(
            "        public static void SetOnEntity(this Entity entity, World world, ComponentDefinition def)"
        );
        sb.AppendLine("        {");
        sb.AppendLine("            bool handled = false;");
        sb.AppendLine("            TrySetCustom(entity, world, def, ref handled);");
        sb.AppendLine("            if (handled) return;");
        sb.AppendLine("");
        sb.AppendLine("            switch (def.TypeName)");
        sb.AppendLine("            {");
        foreach (var comp in uniqueComponents)
        {
            if (ShouldUseCustomFactory(comp))
                continue;
            // Uses the Generic Create to get the struct, then sets it.
            // Minimal overhead.
            sb.AppendLine($"                case \"{comp.Name}\":");
            sb.AppendLine(
                $"                    world.Set<{comp.ToDisplayString()}>(entity, Create<{comp.ToDisplayString()}>(def));"
            );
            sb.AppendLine("                    break;");
        }
        sb.AppendLine(
            "                default: throw new ArgumentException($\"Unknown component: {def.TypeName}\");"
        );
        sb.AppendLine("            }");
        sb.AppendLine("        }");

        sb.AppendLine("");

        // --- METHOD 5: SetFromValue (boxed component → typed Set<T>) ---
        sb.AppendLine(
            "        public static void SetFromValue(Entity entity, World world, object component)"
        );
        sb.AppendLine("        {");
        sb.AppendLine("            switch (component)");
        sb.AppendLine("            {");
        foreach (var comp in uniqueComponents)
        {
            sb.AppendLine(
                $"                case {comp.ToDisplayString()} val:"
            );
            sb.AppendLine(
                $"                    world.Set<{comp.ToDisplayString()}>(entity, val);"
            );
            sb.AppendLine("                    break;");
        }
        sb.AppendLine(
            "                default: throw new ArgumentException($\"Unknown component type: {component.GetType().Name}\");"
        );
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("");

        // --- METHOD 6: CloneComponent (returns a copy of a boxed component) ---
        sb.AppendLine("        public static object CloneComponent(object component)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (component)");
        sb.AppendLine("            {");
        foreach (var comp in uniqueComponents)
        {
            if (ShouldUseCustomFactory(comp))
            {
                // GraphicalTileList needs array cloning; Composite types are uncacheable
                if (comp.Name == "GraphicalTileList")
                {
                    sb.AppendLine($"                case {comp.ToDisplayString()} val:");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    var clone = val;");
                    sb.AppendLine(
                        "                    if (val.Tiles != null) clone.Tiles = (epoch.ECS.GraphicalTile[])val.Tiles.Clone();"
                    );
                    sb.AppendLine("                    return clone;");
                    sb.AppendLine("                }");
                }
                else
                {
                    // CompositeControllerComponent, CompositePartComponent — uncacheable
                    sb.AppendLine($"                case {comp.ToDisplayString()}:");
                    sb.AppendLine("                    return null;");
                }
            }
            else
            {
                // Value types: boxing already copied the struct, just re-box
                sb.AppendLine(
                    $"                case {comp.ToDisplayString()} val: return val;"
                );
            }
        }
        sb.AppendLine(
            "                default: throw new ArgumentException($\"Unknown component type: {component.GetType().Name}\");"
        );
        sb.AppendLine("            }");
        sb.AppendLine("        }");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("ComponentFactory.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static bool ShouldUseCustomFactory(INamedTypeSymbol comp)
    {
        var attr = comp.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "ComponentAttribute");
        if (attr != null)
        {
            var arg = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "UseCustomFactory");
            if (arg.Value.Value is bool b)
                return b;
        }
        return false;
    }

    private static void GenerateParsingLogic(
        StringBuilder sb,
        INamedTypeSymbol comp,
        string varName
    )
    {
        var members = comp.GetMembers()
            .Where(m =>
                (
                    m is IFieldSymbol f
                    && !f.IsReadOnly
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

            sb.AppendLine(
                $"                if (def.TryGet(\"{member.Name}\", out var val_{member.Name}))"
            );
            sb.AppendLine(
                $"                    {varName}.{member.Name} = Utils.{parseFunc}(val_{member.Name});"
            );
        }
    }

    private static string GetParseMethodName(ITypeSymbol type)
    {
        if (
            type is INamedTypeSymbol namedType
            && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
        )
        {
            type = namedType.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum)
            return $"ParseEnum<{type.ToDisplayString()}>";

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
