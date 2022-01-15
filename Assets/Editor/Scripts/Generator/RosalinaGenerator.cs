﻿using Assets.Editor.Scripts.Generator.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

internal static class RosalinaGenerator
{
    private static readonly string GeneratedCodeHeader = @$"//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by the Rosalina Code Generator tool.
//     Version: {RosalinaConstants.Version}
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

";

    private const string DocumentFieldName = "_document";
    private const string RootVisualElementPropertyName = "Root";
    private const string InitializeDocumentMethodName = "InitializeDocument";

    /// <summary>
    /// Generates the UI document code behind.
    /// </summary>
    /// <param name="uiDocumentPath">UI Document path.</param>
    public static void Generate(UIDocumentAsset document)
    {
        // Parse document
        UxmlNode uiDocumentRootNode = RosalinaUXMLParser.ParseUIDocument(document.FullPath);
        IEnumerable<UIPropertyDescriptor> namedNodes = uiDocumentRootNode.Children
            .FlattenTree(x => x.Children)
            .Where(x => x.HasName)
            .Select(x => new UIPropertyDescriptor(x.Type, x.Name))
            .ToList();

        MemberDeclarationSyntax documentVariable = CreateDocumentVariable();
        MemberDeclarationSyntax visualElementProperty = CreateVisualElementRootProperty();

        InitializationStatement[] statements = GenerateInitializeStatements(namedNodes);
        FieldDeclarationSyntax[] privateFieldsStatements = statements.Select(x => x.PrivateField).ToArray();
        StatementSyntax[] initializationStatements = statements.Select(x => x.Statement).ToArray();

        MethodDeclarationSyntax initializeMethod = GenerateInitializeMethod()
            .WithBody(SyntaxFactory.Block(initializationStatements));

        MemberDeclarationSyntax[] classMembers = new[] { documentVariable }
            .Concat(privateFieldsStatements)
            .Append(visualElementProperty)
            .Append(initializeMethod)
            .ToArray();

        UsingDirectiveSyntax[] usings = GetDefaultUsingDirectives();
        ClassDeclarationSyntax @class = SyntaxFactory.ClassDeclaration(document.Name)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword))
            .AddBaseListTypes(
                SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseName(typeof(MonoBehaviour).Name))
            )
            .AddMembers(classMembers);

        CompilationUnitSyntax compilationUnit = SyntaxFactory.CompilationUnit()
            .AddUsings(usings)
            .AddMembers(@class);

        string code = compilationUnit
            .NormalizeWhitespace()
            .ToFullString();
        string generatedCode = GeneratedCodeHeader + code;

        File.WriteAllText(document.GeneratedFileOutputPath, generatedCode);
    }

    private static UsingDirectiveSyntax[] GetDefaultUsingDirectives()
    {
        return new UsingDirectiveSyntax[]
        {
            SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("UnityEngine")),
            SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("UnityEngine.UIElements"))
        };
    }

    private static MemberDeclarationSyntax CreateDocumentVariable()
    {
        string documentPropertyTypeName = typeof(UIDocument).Name;

        FieldDeclarationSyntax documentField = RosalinaSyntaxFactory.CreateField(documentPropertyTypeName, DocumentFieldName, SyntaxKind.PrivateKeyword)
            .AddAttributeLists(
                SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Attribute(SyntaxFactory.ParseName(typeof(SerializeField).Name))
                    )
                )
            );

        return documentField;
    }

    private static MemberDeclarationSyntax CreateVisualElementRootProperty()
    {
        string propertyTypeName = typeof(VisualElement).Name;
        string documentFieldName = $"{DocumentFieldName}?";
        const string documentRootVisualElementPropertyName = "rootVisualElement";

        return RosalinaSyntaxFactory.CreateProperty(propertyTypeName, RootVisualElementPropertyName, SyntaxKind.PublicKeyword)
            .AddAccessorListAccessors(
                SyntaxFactory.AccessorDeclaration(
                    SyntaxKind.GetAccessorDeclaration,
                    SyntaxFactory.Block(
                        SyntaxFactory.ReturnStatement(
                            SyntaxFactory.Token(SyntaxKind.ReturnKeyword),
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName(documentFieldName),
                                SyntaxFactory.IdentifierName(documentRootVisualElementPropertyName)),
                            SyntaxFactory.Token(SyntaxKind.SemicolonToken)
                        )
                    )
                )
            );
    }

    private static MemberAccessExpressionSyntax CreateRootQueryMethodAccessor()
    {
        string propertyName = $"{RootVisualElementPropertyName}?";
        const string queryMethodName = "Q";

        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName(propertyName),
            SyntaxFactory.Token(SyntaxKind.DotToken),
            SyntaxFactory.IdentifierName(queryMethodName)
        );
    }

    private static MethodDeclarationSyntax GenerateInitializeMethod()
    {
        SyntaxToken methodModifier = SyntaxFactory.Token(SyntaxKind.PublicKeyword);
        TypeSyntax methodReturnType = SyntaxFactory.ParseTypeName("void");

        MethodDeclarationSyntax initializeMethod = SyntaxFactory
            .MethodDeclaration(methodReturnType, InitializeDocumentMethodName)
            .AddModifiers(methodModifier);

        return initializeMethod;
    }

    private static InitializationStatement[] GenerateInitializeStatements(IEnumerable<UIPropertyDescriptor> properties)
    {
        var documentQueryMethodAccess = CreateRootQueryMethodAccessor();
        var statements = new List<InitializationStatement>();

        foreach (var property in properties)
        {
            FieldDeclarationSyntax field = RosalinaSyntaxFactory.CreateField(property.Type, property.PrivateName, SyntaxKind.PrivateKeyword);

            var argumentList = SyntaxFactory.SeparatedList(new[]
            {
                SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(property.Name)
                    )
                )
            });
            var cast = SyntaxFactory.CastExpression(
                SyntaxFactory.ParseName(property.Type),
                SyntaxFactory.InvocationExpression(documentQueryMethodAccess, SyntaxFactory.ArgumentList(argumentList))
            );
            var statement = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(property.PrivateName),
                    cast
                )
            );

            statements.Add(new InitializationStatement(statement, field));
        }

        return statements.ToArray();
    }

    private struct InitializationStatement
    {
        public StatementSyntax Statement { get; }

        public FieldDeclarationSyntax PrivateField { get; }

        public InitializationStatement(StatementSyntax statement, FieldDeclarationSyntax privateField)
        {
            Statement = statement;
            PrivateField = privateField;
        }
    }
}