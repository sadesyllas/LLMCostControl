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
            // - Any file inside Migrations/
            // - Generated files ending with .Designer.cs or ModelSnapshot.cs
            // - Program.cs
            if (pathParts.Contains("obj") || pathParts.Contains("bin") || pathParts.Contains("Migrations"))
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

            // Find all type declarations (class, struct, interface, enum, record)
            var typeDeclarations = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .Where(t => !t.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any())
                .ToList();

            if (typeDeclarations.Count > 1)
            {
                var names = string.Join(", ", typeDeclarations.Select(t => t.Identifier.Text));
                violations.Add($"{relativePath}: declares multiple top-level types ({names})");
            }
            else if (typeDeclarations.Count == 1)
            {
                var typeName = typeDeclarations[0].Identifier.Text;
                var expectedFileName = Path.GetFileNameWithoutExtension(file);
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
    public void Test_OneTopLevelTypePerFileStandard_WithFixture(string code, bool shouldViolate)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var topLevelTypes = root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Where(t => !t.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any())
            .ToList();

        var hasMultiple = topLevelTypes.Count > 1;
        hasMultiple.Should().Be(shouldViolate);
    }
}
