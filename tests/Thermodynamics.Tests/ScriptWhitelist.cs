using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Thermodynamics.Tests
{
    public static class ScriptWhitelist
    {
        public const string TranscribedFrom = "Space Engineers, Bin64 of 2026-08-14, read 2026-08-23";

        public static readonly HashSet<string> AllowedNamespaces = new HashSet<string>
        {
            "System.Collections",
            "System.Collections.Generic",
            "System.Text",
            "System.Text.RegularExpressions",
            "System.Globalization",

            "System.Linq",
            "System.Collections.Concurrent",

            "System.Timers",

            "System.Collections.Immutable",
        };

        public static readonly HashSet<string> AllowedTypes = new HashSet<string>
        {
            "Object", "IDisposable", "String", "StringComparison", "Math", "Enum",
            "Int32", "Int16", "Int64", "UInt32", "UInt16", "UInt64",
            "Double", "Single", "Boolean", "Char", "Byte", "SByte", "Decimal",
            "DateTime", "TimeSpan", "Array",

            "XmlElementAttribute", "XmlAttributeAttribute", "XmlArrayAttribute",
            "XmlArrayItemAttribute", "XmlAnyAttributeAttribute", "XmlAnyElementAttribute",
            "XmlAnyElementAttributes", "XmlArrayItemAttributes", "XmlAttributeEventArgs",
            "XmlAttributeOverrides", "XmlAttributes", "XmlChoiceIdentifierAttribute",
            "XmlElementAttributes", "XmlElementEventArgs", "XmlEnumAttribute",
            "XmlIgnoreAttribute", "XmlIncludeAttribute", "XmlRootAttribute", "XmlTextAttribute",
            "XmlTypeAttribute",

            "RuntimeHelpers", "BinaryReader", "BinaryWriter",
            "NullReferenceException", "ArgumentException", "ArgumentNullException",
            "InvalidOperationException", "FormatException", "Exception", "DivideByZeroException",
            "InvalidCastException", "FileNotFoundException", "NotSupportedException",
            "Nullable", "StringComparer", "IEquatable", "IComparable", "BitConverter",
            "FlagsAttribute", "Path", "Random", "Convert", "StringSplitOptions", "DateTimeKind",
            "MidpointRounding", "EventArgs", "Buffer",
            "INotifyPropertyChanging", "PropertyChangingEventHandler", "PropertyChangingEventArgs",
            "INotifyPropertyChanged", "PropertyChangedEventHandler", "PropertyChangedEventArgs",

            "Action", "Func",

            "Stream", "TextWriter", "TextReader",

            "TraceEventType", "AssemblyProductAttribute", "AssemblyDescriptionAttribute",
            "AssemblyConfigurationAttribute", "AssemblyCompanyAttribute", "AssemblyCultureAttribute",
            "AssemblyVersionAttribute", "AssemblyFileVersionAttribute", "AssemblyCopyrightAttribute",
            "AssemblyTrademarkAttribute", "AssemblyTitleAttribute", "ComVisibleAttribute",
            "DefaultValueAttribute", "SerializableAttribute", "GuidAttribute",
            "StructLayoutAttribute", "LayoutKind", "Guid",

            "Monitor", "AutoResetEvent", "ManualResetEvent", "Interlocked",
            "Stopwatch", "ConditionalAttribute", "Version", "ObsoleteAttribute",

            "MemberInfo", "Type", "ValueType", "Environment", "Delegate",

            "Void", "IntPtr", "UIntPtr",
        };

        public struct Finding
        {
            public string File;
            public int Line;
            public string Name;

            public string Namespaces;

/// <summary>ToString operation.</summary>
            public override string ToString()
            {
                return File + ":" + Line + "  " + Name + "  (" + Namespaces + ")";
            }
        }

/// <summary>SourceRoot operation.</summary>
        public static string SourceRoot()
        {
            string root = Harness.ShippedBlocks.RepoRoot();
            if (root == null) return null;

            string candidate = Path.Combine(root, "Thermodynamics");
            return Directory.Exists(candidate) ? candidate : null;
        }

/// <summary>FrameworkFacade operation.</summary>
        public static string FrameworkFacade()
        {
            string content = Harness.GameBlocks.ContentPath();
            if (content == null) return null;

            DirectoryInfo install = Directory.GetParent(content);            // Content
            if (install != null) install = install.Parent;                   // the install root
            if (install == null) return null;

            string facade = Path.Combine(install.FullName, "Bin64", "netstandard.dll");
            return File.Exists(facade) ? facade : null;
        }

/// <summary>ProhibitedNames operation.</summary>
        public static Dictionary<string, string> ProhibitedNames()
        {
            Dictionary<string, string> prohibited = new Dictionary<string, string>();
            Dictionary<string, List<string>> byName = new Dictionary<string, List<string>>();

/// <summary>FrameworkFacade operation.</summary>
            string facade = FrameworkFacade();
            if (facade == null) return prohibited;

            using (PEReader reader = new PEReader(File.OpenRead(facade)))
            {
                if (!reader.HasMetadata) return prohibited;
                MetadataReader metadata = reader.GetMetadataReader();

                foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
                {
                    TypeDefinition definition = metadata.GetTypeDefinition(handle);
                    TypeAttributes visibility = definition.Attributes & TypeAttributes.VisibilityMask;
                    if (visibility != TypeAttributes.Public) continue;

                    Record(byName, metadata.GetString(definition.Namespace),
                        metadata.GetString(definition.Name));
                }

                foreach (ExportedTypeHandle handle in metadata.ExportedTypes)
                {
                    ExportedType exported = metadata.GetExportedType(handle);
                    Record(byName, metadata.GetString(exported.Namespace),
                        metadata.GetString(exported.Name));
                }
            }

/// <summary>GameTypeNames operation.</summary>
            HashSet<string> shadowed = GameTypeNames();
/// <summary>DeclaredByTheMod operation.</summary>
            HashSet<string> declared = DeclaredByTheMod();

            foreach (KeyValuePair<string, List<string>> entry in byName)
            {
                if (AllowedTypes.Contains(entry.Key)) continue;
                if (entry.Value.Any(AllowedNamespaces.Contains)) continue;
                if (shadowed.Contains(entry.Key)) continue;
                if (declared.Contains(entry.Key)) continue;

                prohibited[entry.Key] = string.Join(", ", entry.Value.Distinct().OrderBy(x => x));
            }

            return prohibited;
        }

/// <summary>GameTypeNames operation.</summary>
        public static HashSet<string> GameTypeNames()
        {
            if (_gameTypeNames != null) return _gameTypeNames;

/// <summary>HashSet operation.</summary>
            HashSet<string> names = new HashSet<string>();
/// <summary>FrameworkFacade operation.</summary>
            string facade = FrameworkFacade();

            if (facade != null)
            {
                string bin = Path.GetDirectoryName(facade);

                foreach (string file in Directory.GetFiles(bin, "*.dll"))
                {
                    string assembly = Path.GetFileName(file);
                    if (!assembly.StartsWith("Sandbox.", StringComparison.Ordinal)
                        && !assembly.StartsWith("VRage", StringComparison.Ordinal)
                        && !assembly.StartsWith("SpaceEngineers", StringComparison.Ordinal)
                        && !assembly.StartsWith("ProtoBuf", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    CollectTypeNames(file, names);
                }
            }

            _gameTypeNames = names;
            return names;
        }

        private static HashSet<string> _gameTypeNames;

/// <summary>CollectTypeNames operation.</summary>
        private static void CollectTypeNames(string file, HashSet<string> names)
        {
            try
            {
                using (PEReader reader = new PEReader(File.OpenRead(file)))
                {
                    if (!reader.HasMetadata) return;
                    MetadataReader metadata = reader.GetMetadataReader();

                    foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
                    {
                        TypeDefinition definition = metadata.GetTypeDefinition(handle);
                        string name = metadata.GetString(definition.Name);

                        int generic = name.IndexOf('`');
                        if (generic >= 0) name = name.Substring(0, generic);
                        names.Add(name);
                    }
                }
            }
            catch (BadImageFormatException)
            {
            }
        }

/// <summary>DeclaredByTheMod operation.</summary>
        public static HashSet<string> DeclaredByTheMod()
        {
/// <summary>HashSet operation.</summary>
            HashSet<string> declared = new HashSet<string>();

            foreach (string path in Sources())
            {
                SyntaxNode root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    BaseTypeDeclarationSyntax type = node as BaseTypeDeclarationSyntax;
                    if (type != null)
                    {
                        declared.Add(type.Identifier.ValueText);
                        continue;
                    }

                    DelegateDeclarationSyntax handler = node as DelegateDeclarationSyntax;
                    if (handler != null) declared.Add(handler.Identifier.ValueText);
                }
            }

            return declared;
        }

/// <summary>Record operation.</summary>
        private static void Record(Dictionary<string, List<string>> byName, string space, string name)
        {
            if (space.Length == 0 || !(space == "System" || space.StartsWith("System."))) return;

            int generic = name.IndexOf('`');
            if (generic >= 0) name = name.Substring(0, generic);

            List<string> spaces;
            if (!byName.TryGetValue(name, out spaces))
            {
/// <summary>List operation.</summary>
                spaces = new List<string>();
                byName[name] = spaces;
            }

            spaces.Add(space);
        }

/// <summary>Sources operation.</summary>
        public static List<string> Sources()
        {
/// <summary>SourceRoot operation.</summary>
            string root = SourceRoot();
            if (root == null) return new List<string>();

            return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }

/// <summary>Scan operation.</summary>
        public static List<Finding> Scan(string path, string text, Dictionary<string, string> prohibited)
        {
/// <summary>List operation.</summary>
            List<Finding> findings = new List<Finding>();

            SyntaxNode root = CSharpSyntaxTree.ParseText(text).GetRoot();
/// <summary>List operation.</summary>
            List<TypeSyntax> types = new List<TypeSyntax>();
            CollectTypes(root, types);

            foreach (TypeSyntax type in types)
            {
                foreach (SimpleNameSyntax name in Names(type))
                {
                    string identifier = name.Identifier.ValueText;

                    string spaces;
                    if (!prohibited.TryGetValue(identifier, out spaces)) continue;

                    if (AllowedTypes.Contains(identifier + "Attribute")) continue;

                    if (name.Parent is QualifiedNameSyntax
                        && ((QualifiedNameSyntax)name.Parent).Right == name
                        && !IsFrameworkQualified((QualifiedNameSyntax)name.Parent))
                    {
                        continue;
                    }

/// <summary>Finding operation.</summary>
                    Finding finding = new Finding();
                    finding.File = path;
                    finding.Line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    finding.Name = identifier;
                    finding.Namespaces = spaces;
                    findings.Add(finding);
                }
            }

            return findings;
        }

/// <summary>IsFrameworkQualified operation.</summary>
        private static bool IsFrameworkQualified(QualifiedNameSyntax qualified)
        {
            NameSyntax left = qualified.Left;
            while (left is QualifiedNameSyntax) left = ((QualifiedNameSyntax)left).Left;

            SimpleNameSyntax simple = left as SimpleNameSyntax;
            return simple != null && simple.Identifier.ValueText == "System";
        }

/// <summary>CollectTypes operation.</summary>
        private static void CollectTypes(SyntaxNode node, List<TypeSyntax> types)
        {
            foreach (SyntaxNode child in node.DescendantNodes())
            {
                if (child is ParameterSyntax) Add(((ParameterSyntax)child).Type, types);
                else if (child is VariableDeclarationSyntax) Add(((VariableDeclarationSyntax)child).Type, types);
                else if (child is MethodDeclarationSyntax) Add(((MethodDeclarationSyntax)child).ReturnType, types);
                else if (child is PropertyDeclarationSyntax) Add(((PropertyDeclarationSyntax)child).Type, types);
                else if (child is IndexerDeclarationSyntax) Add(((IndexerDeclarationSyntax)child).Type, types);
                else if (child is DelegateDeclarationSyntax) Add(((DelegateDeclarationSyntax)child).ReturnType, types);
                else if (child is OperatorDeclarationSyntax) Add(((OperatorDeclarationSyntax)child).ReturnType, types);
                else if (child is ConversionOperatorDeclarationSyntax) Add(((ConversionOperatorDeclarationSyntax)child).Type, types);
                else if (child is ObjectCreationExpressionSyntax) Add(((ObjectCreationExpressionSyntax)child).Type, types);
                else if (child is CastExpressionSyntax) Add(((CastExpressionSyntax)child).Type, types);
                else if (child is TypeOfExpressionSyntax) Add(((TypeOfExpressionSyntax)child).Type, types);
                else if (child is DefaultExpressionSyntax) Add(((DefaultExpressionSyntax)child).Type, types);
                else if (child is SizeOfExpressionSyntax) Add(((SizeOfExpressionSyntax)child).Type, types);
                else if (child is CatchDeclarationSyntax) Add(((CatchDeclarationSyntax)child).Type, types);
                else if (child is TypeConstraintSyntax) Add(((TypeConstraintSyntax)child).Type, types);
                else if (child is SimpleBaseTypeSyntax) Add(((SimpleBaseTypeSyntax)child).Type, types);
/// <summary>if operation.</summary>
                else if (child is TypeArgumentListSyntax)
                {
                    foreach (TypeSyntax argument in ((TypeArgumentListSyntax)child).Arguments)
                    {
                        Add(argument, types);
                    }
                }
/// <summary>if operation.</summary>
                else if (child is AttributeSyntax)
                {
                    Add(((AttributeSyntax)child).Name, types);
                }
/// <summary>if operation.</summary>
                else if (child is BinaryExpressionSyntax)
                {
                    BinaryExpressionSyntax binary = (BinaryExpressionSyntax)child;
                    if (binary.IsKind(SyntaxKind.AsExpression) || binary.IsKind(SyntaxKind.IsExpression))
                    {
                        Add(binary.Right as TypeSyntax, types);
                    }
                }
            }
        }

/// <summary>Adds a .</summary>
        private static void Add(TypeSyntax type, List<TypeSyntax> types)
        {
            if (type != null) types.Add(type);
        }

/// <summary>Names operation.</summary>
        private static IEnumerable<SimpleNameSyntax> Names(TypeSyntax type)
        {
/// <summary>List operation.</summary>
            List<SimpleNameSyntax> names = new List<SimpleNameSyntax>();
            Walk(type, names);
            return names;
        }

/// <summary>Walk operation.</summary>
        private static void Walk(SyntaxNode node, List<SimpleNameSyntax> names)
        {
            if (node == null) return;

            if (node is PredefinedTypeSyntax) return;

            if (node is SimpleNameSyntax)
            {
                names.Add((SimpleNameSyntax)node);

                GenericNameSyntax generic = node as GenericNameSyntax;
                if (generic != null)
                {
                    foreach (TypeSyntax argument in generic.TypeArgumentList.Arguments)
                    {
                        Walk(argument, names);
                    }
                }

                return;
            }

            foreach (SyntaxNode child in node.ChildNodes()) Walk(child, names);
        }
    }
}
