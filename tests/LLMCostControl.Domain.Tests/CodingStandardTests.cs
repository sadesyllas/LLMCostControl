using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using FluentAssertions;

namespace LLMCostControl.Domain.Tests;

/// <summary>
/// Verifies coding standard compliance (§17, M20).
/// Enforces one top-level type definition per file and that the file name matches the type it declares.
/// </summary>
public sealed class CodingStandardTests
{
    [Fact]
    public void OneTopLevelTypePerFileStandard_ShouldBeEnforced()
    {
        // 1. Locate src directory
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "LLMCostControl.slnx")))
        {
            currentDir = currentDir.Parent;
        }
        currentDir.Should().NotBeNull("Solution root directory containing LLMCostControl.slnx should be found");
        var srcDir = Path.Combine(currentDir!.FullName, "src");
        Directory.Exists(srcDir).Should().BeTrue($"src directory should exist at {srcDir}");

        // 2. Collect files and violations
        var violations = new List<string>();
        var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);

        foreach (var file in csFiles)
        {
            var relativePath = Path.GetRelativePath(srcDir, file);
            var pathParts = relativePath.Split(Path.DirectorySeparatorChar);

            // Exemptions:
            // - Any file inside obj/ or bin/
            // - Generated files ending with .Designer.cs or ModelSnapshot.cs
            // - Program.cs
            if (pathParts.Contains("obj") || pathParts.Contains("bin"))
            {
                continue;
            }

            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();

            var topLevelTypes = GetTopLevelTypeNames(root);

            if (topLevelTypes.Count > 1)
            {
                var names = string.Join(", ", topLevelTypes);
                violations.Add($"{relativePath}: declares multiple top-level types ({names})");
            }
            else if (topLevelTypes.Count == 1)
            {
                var typeName = topLevelTypes[0];
                var expectedFileName = Path.GetFileNameWithoutExtension(file);

                // For EF migrations in a Migrations folder, strip the 14-digit timestamp prefix
                if (pathParts.Contains("Migrations") && expectedFileName.Length > 15 && expectedFileName[14] == '_' && long.TryParse(expectedFileName.Substring(0, 14), out _))
                {
                    expectedFileName = expectedFileName.Substring(15);
                }

                if (!expectedFileName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{relativePath}: top-level type '{typeName}' does not match file name '{fileName}'");
                }
            }
        }

        // Assert no violations
        if (violations.Count > 0)
        {
            var message = "Coding Standard Violation: Each production .cs file must declare at most one top-level type, and its name must match the file name.\n" +
                          string.Join("\n", violations);
            Assert.Fail(message);
        }
    }

    [Theory]
    [InlineData("public class A {} public class B {}", true)]
    [InlineData("public class A { public class B {} }", false)]
    [InlineData("namespace N { public class A {} }", false)]
    [InlineData("namespace N { public class A {} public class B {} }", true)]
    [InlineData("public delegate void D(); public class A {}", true)]
    [InlineData("public delegate void D();", false)]
    public void Test_OneTopLevelTypePerFileStandard_WithFixture(string code, bool shouldViolate)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var topLevelTypes = GetTopLevelTypeNames(root);

        var hasMultiple = topLevelTypes.Count > 1;
        hasMultiple.Should().Be(shouldViolate);
    }

    internal static IReadOnlyList<string> GetTopLevelTypeNames(SyntaxNode root)
    {
        return root.DescendantNodes()
            .Where(node => node is BaseTypeDeclarationSyntax || node is DelegateDeclarationSyntax)
            .Where(node => !node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any())
            .Select(node =>
            {
                if (node is BaseTypeDeclarationSyntax baseType)
                    return baseType.Identifier.Text;
                if (node is DelegateDeclarationSyntax del)
                    return del.Identifier.Text;
                return string.Empty;
            })
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
    }
}
