﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.CodeAnalysis.AddImports;
using Microsoft.CodeAnalysis.CSharp.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Host.Mef;
using Roslyn.Utilities;

#if CODE_STYLE
using OptionSet = Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions;
#else
using Microsoft.CodeAnalysis.Options;
#endif

namespace Microsoft.CodeAnalysis.CSharp.AddImports
{
    [ExportLanguageService(typeof(IAddImportsService), LanguageNames.CSharp), Shared]
    internal class CSharpAddImportsService : AbstractAddImportsService<
        CompilationUnitSyntax, BaseNamespaceDeclarationSyntax, UsingDirectiveSyntax, ExternAliasDirectiveSyntax>
    {
        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public CSharpAddImportsService()
        {
        }

        protected override bool PlaceImportsInsideNamespaces(OptionSet options)
        {
            return options.GetOption(CSharpCodeStyleOptions.PreferredUsingDirectivePlacement).Value == AddImportPlacement.InsideNamespace;
        }

        // C# doesn't have global imports.
        protected override ImmutableArray<SyntaxNode> GetGlobalImports(Compilation compilation, SyntaxGenerator generator)
            => ImmutableArray<SyntaxNode>.Empty;

        protected override SyntaxNode? GetAlias(UsingDirectiveSyntax usingOrAlias)
            => usingOrAlias.Alias;

        protected override bool IsStaticUsing(UsingDirectiveSyntax usingOrAlias)
            => usingOrAlias.StaticKeyword != default;

        protected override SyntaxNode Rewrite(
            ExternAliasDirectiveSyntax[] externAliases,
            UsingDirectiveSyntax[] usingDirectives,
            UsingDirectiveSyntax[] staticUsingDirectives,
            UsingDirectiveSyntax[] aliasDirectives,
            SyntaxNode externContainer,
            SyntaxNode usingContainer,
            SyntaxNode staticUsingContainer,
            SyntaxNode aliasContainer,
            bool placeSystemNamespaceFirst,
            bool allowInHiddenRegions,
            SyntaxNode root,
            CancellationToken cancellationToken)
        {
            var rewriter = new Rewriter(
                externAliases, usingDirectives, staticUsingDirectives, aliasDirectives,
                externContainer, usingContainer, staticUsingContainer, aliasContainer,
                placeSystemNamespaceFirst, allowInHiddenRegions, cancellationToken);

            var newRoot = rewriter.Visit(root);
            return newRoot;
        }

        protected override SyntaxList<UsingDirectiveSyntax> GetUsingsAndAliases(SyntaxNode node)
            => node switch
            {
                CompilationUnitSyntax c => c.Usings,
                BaseNamespaceDeclarationSyntax n => n.Usings,
                _ => default,
            };

        protected override SyntaxList<ExternAliasDirectiveSyntax> GetExterns(SyntaxNode node)
            => node switch
            {
                CompilationUnitSyntax c => c.Externs,
                BaseNamespaceDeclarationSyntax n => n.Externs,
                _ => default,
            };

        protected override bool IsEquivalentImport(SyntaxNode a, SyntaxNode b)
            => SyntaxFactory.AreEquivalent(a, b, kind => kind == SyntaxKind.NullableDirectiveTrivia);

        private class Rewriter : CSharpSyntaxRewriter
        {
            private readonly bool _placeSystemNamespaceFirst;
            private readonly bool _allowInHiddenRegions;
            private readonly CancellationToken _cancellationToken;
            private readonly SyntaxNode _externContainer;
            private readonly SyntaxNode _usingContainer;
            private readonly SyntaxNode _aliasContainer;
            private readonly SyntaxNode _staticUsingContainer;
            private readonly UsingDirectiveSyntax[] _aliasDirectives;
            private readonly ExternAliasDirectiveSyntax[] _externAliases;
            private readonly UsingDirectiveSyntax[] _usingDirectives;
            private readonly UsingDirectiveSyntax[] _staticUsingDirectives;

            public Rewriter(
                ExternAliasDirectiveSyntax[] externAliases,
                UsingDirectiveSyntax[] usingDirectives,
                UsingDirectiveSyntax[] staticUsingDirectives,
                UsingDirectiveSyntax[] aliasDirectives,
                SyntaxNode externContainer,
                SyntaxNode usingContainer,
                SyntaxNode aliasContainer,
                SyntaxNode staticUsingContainer,
                bool placeSystemNamespaceFirst,
                bool allowInHiddenRegions,
                CancellationToken cancellationToken)
            {
                _externAliases = externAliases;
                _usingDirectives = usingDirectives;
                _staticUsingDirectives = staticUsingDirectives;
                _aliasDirectives = aliasDirectives;
                _externContainer = externContainer;
                _usingContainer = usingContainer;
                _aliasContainer = aliasContainer;
                _staticUsingContainer = staticUsingContainer;
                _placeSystemNamespaceFirst = placeSystemNamespaceFirst;
                _allowInHiddenRegions = allowInHiddenRegions;
                _cancellationToken = cancellationToken;
            }

            [return: NotNullIfNotNull("node")]
            public override SyntaxNode? Visit(SyntaxNode? node)
                => base.Visit(node);

            public override SyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
                => VisitBaseNamespaceDeclaration(node, (BaseNamespaceDeclarationSyntax?)base.VisitNamespaceDeclaration(node));

            public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
                => VisitBaseNamespaceDeclaration(node, (BaseNamespaceDeclarationSyntax?)base.VisitFileScopedNamespaceDeclaration(node));

            private SyntaxNode VisitBaseNamespaceDeclaration(
                BaseNamespaceDeclarationSyntax node, BaseNamespaceDeclarationSyntax? rewritten)
            {
                Contract.ThrowIfNull(rewritten);

                // recurse downwards so we visit inner namespaces first.
                if (!node.CanAddUsingDirectives(_allowInHiddenRegions, _cancellationToken))
                {
                    return rewritten;
                }

                if (node == _aliasContainer)
                {
                    rewritten = rewritten.AddUsingDirectives(_aliasDirectives, _placeSystemNamespaceFirst);
                }

                if (node == _usingContainer)
                {
                    rewritten = rewritten.AddUsingDirectives(_usingDirectives, _placeSystemNamespaceFirst);
                }

                if (node == _staticUsingContainer)
                {
                    rewritten = rewritten.AddUsingDirectives(_staticUsingDirectives, _placeSystemNamespaceFirst);
                }

                if (node == _externContainer)
                {
                    rewritten = rewritten.AddExterns(_externAliases);
                }

                return rewritten;
            }

            public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
            {
                // recurse downwards so we visit inner namespaces first.
                var rewritten = (CompilationUnitSyntax)(base.VisitCompilationUnit(node) ?? throw ExceptionUtilities.Unreachable);

                if (!node.CanAddUsingDirectives(_allowInHiddenRegions, _cancellationToken))
                {
                    return rewritten;
                }

                if (node == _aliasContainer)
                {
                    rewritten = rewritten.AddUsingDirectives(_aliasDirectives, _placeSystemNamespaceFirst);
                }

                if (node == _usingContainer)
                {
                    rewritten = rewritten.AddUsingDirectives(_usingDirectives, _placeSystemNamespaceFirst);
                }

                if (node == _staticUsingContainer)
                {
                    rewritten = rewritten.AddUsingDirectives(_staticUsingDirectives, _placeSystemNamespaceFirst);
                }

                if (node == _externContainer)
                {
                    rewritten = rewritten.AddExterns(_externAliases);
                }

                return rewritten;
            }
        }
    }
}
