using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Thermodynamics.Harness;
using Xunit;

namespace Thermodynamics.Tests
{
    [Trait("speed", "slow")]
    public class UncalledCodeTests
    {
/// <summary>SourceFiles operation.</summary>
        private static List<string> SourceFiles()
        {
/// <summary>List operation.</summary>
            List<string> files = new List<string>();
            string root = ShippedBlocks.RepoRoot();

            foreach (string folder in new[] { "Thermodynamics/Content/Data/", "tests" })
            {
                foreach (string path in Directory.GetFiles(Path.Combine(root, folder), "*.cs",
                    SearchOption.AllDirectories))
                {
                    string relative = path.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
                    if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;
                    files.Add(path);
                }
            }

            return files;
        }

/// <summary>IsVendored operation.</summary>
        private static bool IsVendored(string relative)
        {
            return relative.Contains("/RichHudFramework/");
        }

        [Fact]
/// <summary>NoPrivateHelperInTheTreeIsCalledByNothing operation.</summary>
        public void NoPrivateHelperInTheTreeIsCalledByNothing()
        {
            string root = ShippedBlocks.RepoRoot();
/// <summary>SourceFiles operation.</summary>
            List<string> files = SourceFiles();

            Dictionary<string, int> mentions = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, SyntaxNode> roots = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);

            foreach (string path in files)
            {
                SyntaxNode tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
                roots[path] = tree;

                foreach (SyntaxToken token in tree.DescendantTokens())
                {
                    if (!token.IsKind(SyntaxKind.IdentifierToken)) continue;

                    int seen;
                    mentions.TryGetValue(token.ValueText, out seen);
                    mentions[token.ValueText] = seen + 1;
                }
            }

/// <summary>List operation.</summary>
            List<string> uncalled = new List<string>();

            foreach (string path in files)
            {
                string relative = path.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (IsVendored(relative)) continue;

                foreach (MethodDeclarationSyntax method in roots[path].DescendantNodes()
                    .OfType<MethodDeclarationSyntax>())
                {
                    if (!IsPrivateOrInternalStatic(method)) continue;

                    string name = method.Identifier.ValueText;
                    if (mentions[name] > 1) continue;

                    uncalled.Add(relative + ":"
                        + (method.GetLocation().GetLineSpan().StartLinePosition.Line + 1)
                        + " " + name);
                }
            }

            Assert.True(uncalled.Count == 0,
                "these are declared and mentioned nowhere else, so nothing calls"
/// <summary>for operation.</summary>
                + " them — delete them, or give them the caller they were written for (`D2`): "
                + string.Join(", ", uncalled.ToArray()));
        }

/// <summary>IsPrivateOrInternalStatic operation.</summary>
        private static bool IsPrivateOrInternalStatic(MethodDeclarationSyntax method)
        {
            bool isStatic = false;
            bool isReachableFromOutside = false;
            bool isElsewhere = false;

            foreach (SyntaxToken modifier in method.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.StaticKeyword)) isStatic = true;
                if (modifier.IsKind(SyntaxKind.PublicKeyword)
                    || modifier.IsKind(SyntaxKind.ProtectedKeyword)) isReachableFromOutside = true;
                if (modifier.IsKind(SyntaxKind.PartialKeyword)
                    || modifier.IsKind(SyntaxKind.ExternKeyword)) isElsewhere = true;
            }

            return isStatic && !isReachableFromOutside && !isElsewhere;
        }
    }
}
