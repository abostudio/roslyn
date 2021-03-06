﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Utilities;
using Microsoft.CodeAnalysis.Formatting.Rules;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Formatting
{
#if MEF
    [ExportFormattingRule(Name, LanguageNames.CSharp)]
#endif
    internal class ElasticTriviaFormattingRule : BaseFormattingRule
    {
        internal const string Name = "CSharp Elastic trivia Formatting Rule";

        public override void AddSuppressOperations(List<SuppressOperation> list, SyntaxNode node, OptionSet optionSet, NextAction<SuppressOperation> nextOperation)
        {
            nextOperation.Invoke(list);

            if (!node.ContainsAnnotations)
            {
                return;
            }

            AddPropertyDeclarationSuppressOperations(list, node);

            AddInitializerSuppressOperations(list, node);
        }

        private static void AddPropertyDeclarationSuppressOperations(List<SuppressOperation> list, SyntaxNode node)
        {
            var basePropertyDeclaration = node as BasePropertyDeclarationSyntax;
            if (basePropertyDeclaration != null &&
                basePropertyDeclaration.AccessorList.Accessors.All(a => a.Body == null) &&
                basePropertyDeclaration.GetAnnotatedTrivia(SyntaxAnnotation.ElasticAnnotation).Any())
            {
                var tokens = basePropertyDeclaration.GetFirstAndLastMemberDeclarationTokensAfterAttributes();

                list.Add(FormattingOperations.CreateSuppressOperation(tokens.Item1, tokens.Item2, SuppressOption.NoWrapping | SuppressOption.IgnoreElastic));
            }
        }

        private void AddInitializerSuppressOperations(List<SuppressOperation> list, SyntaxNode node)
        {
            var initializer = GetInitializerNode(node);
            var lastTokenOfType = GetLastTokenOfType(node);
            if (initializer != null && lastTokenOfType != null)
            {
                AddSuppressWrappingIfOnSingleLineOperation(list, lastTokenOfType.Value, initializer.CloseBraceToken, SuppressOption.IgnoreElastic);
                return;
            }

            var anonymousCreationNode = node as AnonymousObjectCreationExpressionSyntax;
            if (anonymousCreationNode != null)
            {
                AddSuppressWrappingIfOnSingleLineOperation(list, anonymousCreationNode.NewKeyword, anonymousCreationNode.CloseBraceToken, SuppressOption.IgnoreElastic);
                return;
            }
        }

        private InitializerExpressionSyntax GetInitializerNode(SyntaxNode node)
        {
            var objectCreationNode = node as ObjectCreationExpressionSyntax;
            if (objectCreationNode != null)
            {
                return objectCreationNode.Initializer;
            }

            var arrayCreationNode = node as ArrayCreationExpressionSyntax;
            if (arrayCreationNode != null)
            {
                return arrayCreationNode.Initializer;
            }

            var implicitArrayNode = node as ImplicitArrayCreationExpressionSyntax;
            if (implicitArrayNode != null)
            {
                return implicitArrayNode.Initializer;
            }

            return null;
        }

        private SyntaxToken? GetLastTokenOfType(SyntaxNode node)
        {
            var objectCreationNode = node as ObjectCreationExpressionSyntax;
            if (objectCreationNode != null)
            {
                return objectCreationNode.Type.GetLastToken();
            }

            var arrayCreationNode = node as ArrayCreationExpressionSyntax;
            if (arrayCreationNode != null)
            {
                return arrayCreationNode.Type.GetLastToken();
            }

            var implicitArrayNode = node as ImplicitArrayCreationExpressionSyntax;
            if (implicitArrayNode != null)
            {
                return implicitArrayNode.CloseBracketToken;
            }

            return null;
        }

        public override AdjustNewLinesOperation GetAdjustNewLinesOperation(SyntaxToken previousToken, SyntaxToken currentToken, OptionSet optionSet, NextOperation<AdjustNewLinesOperation> nextOperation)
        {
            var operation = nextOperation.Invoke();
            if (operation == null)
            {
                // If there are more than one Type Parameter Constraint Clause then each go in separate line
                if (CommonFormattingHelpers.HasAnyWhitespaceElasticTrivia(previousToken, currentToken) &&
                    currentToken.RawKind == (int)SyntaxKind.WhereKeyword &&
                    currentToken.IsParentKind(SyntaxKind.TypeParameterConstraintClause))
                {
                    // Check if there is another TypeParameterConstraintClause before
                    if (previousToken.Parent.Ancestors().OfType<TypeParameterConstraintClauseSyntax>().Any())
                    {
                        return CreateAdjustNewLinesOperation(1, AdjustNewLinesOption.PreserveLines);
                    }

                    // Check if there is another TypeParameterConstraintClause after
                    var firstTokenAfterTypeConstraint = currentToken.Parent.GetLastToken().GetNextToken();
                    var lastTokenForTypeConstraint = currentToken.Parent.GetLastToken().GetNextToken();
                    if (CommonFormattingHelpers.HasAnyWhitespaceElasticTrivia(lastTokenForTypeConstraint, firstTokenAfterTypeConstraint) &&
                        firstTokenAfterTypeConstraint.RawKind == (int)SyntaxKind.WhereKeyword &&
                        firstTokenAfterTypeConstraint.IsParentKind(SyntaxKind.TypeParameterConstraintClause))
                    {
                        return CreateAdjustNewLinesOperation(1, AdjustNewLinesOption.PreserveLines);
                    }
                }

                return null;
            }

            // if operation is already forced, return as it is.
            if (operation.Option == AdjustNewLinesOption.ForceLines)
            {
                return operation;
            }

            if (!CommonFormattingHelpers.HasAnyWhitespaceElasticTrivia(previousToken, currentToken))
            {
                return operation;
            }

            var betweenMemberOperation = GetAdjustNewLinesOperationBetweenMembers((SyntaxToken)previousToken, (SyntaxToken)currentToken);
            if (betweenMemberOperation != null)
            {
                return betweenMemberOperation;
            }

            var line = Math.Max(LineBreaksAfter(previousToken, currentToken), operation.Line);
            if (line == 0)
            {
                return CreateAdjustNewLinesOperation(0, AdjustNewLinesOption.PreserveLines);
            }

            return CreateAdjustNewLinesOperation(line, AdjustNewLinesOption.ForceLines);
        }

        private AdjustNewLinesOperation GetAdjustNewLinesOperationBetweenMembers(SyntaxToken previousToken, SyntaxToken currentToken)
        {
            if (!FormattingRangeHelper.InBetweenTwoMembers(previousToken, currentToken))
            {
                return null;
            }

            var previousMember = FormattingRangeHelper.GetEnclosingMember(previousToken);
            var nextMember = FormattingRangeHelper.GetEnclosingMember(currentToken);
            if (previousMember == null || nextMember == null)
            {
                return null;
            }

            // see whether first non whitespace trivia after before the current member is a comment or not
            var triviaList = currentToken.LeadingTrivia;
            var firstNonWhitespaceTrivia = triviaList.FirstOrDefault(trivia => !IsWhitespace(trivia));
            if (firstNonWhitespaceTrivia.IsRegularOrDocComment())
            {
                // the first one is a comment, add two more lines than existing number of lines
                var numberOfLines = GetNumberOfLines(triviaList);
                return CreateAdjustNewLinesOperation(numberOfLines + 2 /* +1 for member itself and +1 for a blank line*/, AdjustNewLinesOption.ForceLines);
            }

            // If we have two members of the same kind, we won't insert a blank line if both members
            // have any content (e.g. accessors bodies, non-empty method bodies, etc.).
            if (previousMember.CSharpKind() == nextMember.CSharpKind())
            {
                // Easy cases:
                if (previousMember.CSharpKind() == SyntaxKind.FieldDeclaration ||
                    previousMember.CSharpKind() == SyntaxKind.EventFieldDeclaration)
                {
                    // Ensure that fields and events are each declared on a separate line.
                    return CreateAdjustNewLinesOperation(1, AdjustNewLinesOption.ForceLines);
                }

                // Don't insert a blank line between properties, indexers or events with no accessors
                if (previousMember is BasePropertyDeclarationSyntax)
                {
                    var previousProperty = (BasePropertyDeclarationSyntax)previousMember;
                    var nextProperty = (BasePropertyDeclarationSyntax)nextMember;

                    if (previousProperty.AccessorList.Accessors.All(a => a.Body == null) &&
                        nextProperty.AccessorList.Accessors.All(a => a.Body == null))
                    {
                        return CreateAdjustNewLinesOperation(1, AdjustNewLinesOption.PreserveLines);
                    }
                }

                // Don't insert a blank line between methods with no bodies
                if (previousMember is BaseMethodDeclarationSyntax)
                {
                    var previousMethod = (BaseMethodDeclarationSyntax)previousMember;
                    var nextMethod = (BaseMethodDeclarationSyntax)nextMember;

                    if (previousMethod.Body == null &&
                        nextMethod.Body == null)
                    {
                        return CreateAdjustNewLinesOperation(1, AdjustNewLinesOption.PreserveLines);
                    }
                }
            }

            return FormattingOperations.CreateAdjustNewLinesOperation(2 /* +1 for member itself and +1 for a blank line*/, AdjustNewLinesOption.ForceLines);
        }

        public override AdjustSpacesOperation GetAdjustSpacesOperation(SyntaxToken previousToken, SyntaxToken currentToken, OptionSet optionSet, NextOperation<AdjustSpacesOperation> nextOperation)
        {
            var operation = nextOperation.Invoke();
            if (operation == null)
            {
                return null;
            }

            // if operation is already forced, return as it is.
            if (operation.Option == AdjustSpacesOption.ForceSpaces)
            {
                return operation;
            }

            if (CommonFormattingHelpers.HasAnyWhitespaceElasticTrivia(previousToken, currentToken))
            {
                // current implementation of engine gives higher priority on new line operations over space operations if
                // two are conflicting.
                // ex) new line operation says add 1 line between tokens, and 
                //     space operation says give 1 space between two tokens (basically means remove new lines)
                //     then, engine will pick new line operation and ignore space operation

                // make every operation forced
                return CreateAdjustSpacesOperation(Math.Max(0, operation.Space), AdjustSpacesOption.ForceSpaces);
            }

            return operation;
        }

        // copied from compiler formatter to have same base forced format
        private int LineBreaksAfter(SyntaxToken previousToken, SyntaxToken currentToken)
        {
            if (currentToken.CSharpKind() == SyntaxKind.None)
            {
                return 0;
            }

            switch (previousToken.CSharpKind())
            {
                case SyntaxKind.None:
                    return 0;

                case SyntaxKind.OpenBraceToken:
                case SyntaxKind.FinallyKeyword:
                    return 1;

                case SyntaxKind.CloseBraceToken:
                    return LineBreaksAfterCloseBrace(currentToken);

                case SyntaxKind.CloseParenToken:
                    return (((previousToken.Parent is StatementSyntax) && currentToken.Parent != previousToken.Parent)
                        || currentToken.CSharpKind() == SyntaxKind.OpenBraceToken) ? 1 : 0;

                case SyntaxKind.CloseBracketToken:
                    if (previousToken.Parent is AttributeListSyntax)
                    {
                        // Assembly and module-level attributes followed by non-attributes should have
                        // a blank line after them.
                        var parent = (AttributeListSyntax)previousToken.Parent;
                        if (parent.Target != null &&
                            (parent.Target.Identifier.IsKindOrHasMatchingText(SyntaxKind.AssemblyKeyword) ||
                             parent.Target.Identifier.IsKindOrHasMatchingText(SyntaxKind.ModuleKeyword)))
                        {
                            if (!(currentToken.Parent is AttributeListSyntax))
                            {
                                return 2;
                            }
                        }

                        if (previousToken.GetAncestor<ParameterSyntax>() == null)
                        {
                            return 1;
                        }
                    }

                    break;

                case SyntaxKind.SemicolonToken:
                    return LineBreaksAfterSemicolon(previousToken, currentToken);

                case SyntaxKind.CommaToken:
                    return previousToken.Parent is EnumDeclarationSyntax ? 1 : 0;

                case SyntaxKind.ElseKeyword:
                    return currentToken.CSharpKind() != SyntaxKind.IfKeyword ? 1 : 0;

                case SyntaxKind.ColonToken:
                    if (previousToken.Parent is LabeledStatementSyntax || previousToken.Parent is SwitchLabelSyntax)
                    {
                        return 1;
                    }

                    break;
            }

            if ((currentToken.CSharpKind() == SyntaxKind.FromKeyword && currentToken.Parent.CSharpKind() == SyntaxKind.FromClause) ||
                (currentToken.CSharpKind() == SyntaxKind.LetKeyword && currentToken.Parent.CSharpKind() == SyntaxKind.LetClause) ||
                (currentToken.CSharpKind() == SyntaxKind.WhereKeyword && currentToken.Parent.CSharpKind() == SyntaxKind.WhereClause) ||
                (currentToken.CSharpKind() == SyntaxKind.JoinKeyword && currentToken.Parent.CSharpKind() == SyntaxKind.JoinClause) ||
                (currentToken.CSharpKind() == SyntaxKind.JoinKeyword && currentToken.Parent.CSharpKind() == SyntaxKind.JoinIntoClause) ||
                (currentToken.CSharpKind() == SyntaxKind.OrderByKeyword && currentToken.Parent.CSharpKind() == SyntaxKind.OrderByClause) ||
                (currentToken.CSharpKind() == SyntaxKind.SelectKeyword && currentToken.Parent.CSharpKind() == SyntaxKind.SelectClause) ||
                (currentToken.CSharpKind() == SyntaxKind.GroupKeyword && currentToken.Parent.CSharpKind() == SyntaxKind.GroupClause))
            {
                return 1;
            }

            switch (currentToken.CSharpKind())
            {
                case SyntaxKind.OpenBraceToken:
                case SyntaxKind.CloseBraceToken:
                case SyntaxKind.ElseKeyword:
                case SyntaxKind.FinallyKeyword:
                    return 1;

                case SyntaxKind.OpenBracketToken:
                    if (currentToken.Parent is AttributeListSyntax)
                    {
                        // Assembly and module-level attributes preceded by non-attributes should have
                        // a blank line separating them.
                        var parent = (AttributeListSyntax)currentToken.Parent;
                        if (parent.Target != null)
                        {
                            if (parent.Target.Identifier == SyntaxFactory.Token(SyntaxKind.AssemblyKeyword) ||
                                parent.Target.Identifier == SyntaxFactory.Token(SyntaxKind.ModuleKeyword))
                            {
                                if (!(previousToken.Parent is AttributeListSyntax))
                                {
                                    return 2;
                                }
                            }
                        }

                        // Attributes on parameters should have no lines between them.
                        if (parent.Parent is ParameterSyntax)
                        {
                            return 0;
                        }

                        return 1;
                    }

                    break;

                case SyntaxKind.WhereKeyword:
                    return previousToken.Parent is TypeParameterListSyntax ? 1 : 0;
            }

            return 0;
        }

        private static int LineBreaksAfterCloseBrace(SyntaxToken nextToken)
        {
            if (nextToken.CSharpKind() == SyntaxKind.CloseBraceToken)
            {
                return 1;
            }
            else if (
                nextToken.CSharpKind() == SyntaxKind.CatchKeyword ||
                nextToken.CSharpKind() == SyntaxKind.FinallyKeyword ||
                nextToken.CSharpKind() == SyntaxKind.ElseKeyword)
            {
                return 1;
            }
            else if (
                nextToken.CSharpKind() == SyntaxKind.WhileKeyword &&
                nextToken.Parent.CSharpKind() == SyntaxKind.DoStatement)
            {
                return 1;
            }
            else if (nextToken.CSharpKind() == SyntaxKind.EndOfFileToken)
            {
                return 0;
            }
            else
            {
                return 2;
            }
        }

        private static int LineBreaksAfterSemicolon(SyntaxToken previousToken, SyntaxToken currentToken)
        {
            if (previousToken.Parent is ForStatementSyntax)
            {
                return 0;
            }
            else if (currentToken.CSharpKind() == SyntaxKind.CloseBraceToken)
            {
                return 1;
            }
            else if (previousToken.Parent is UsingDirectiveSyntax)
            {
                return currentToken.Parent is UsingDirectiveSyntax ? 1 : 2;
            }
            else if (previousToken.Parent is ExternAliasDirectiveSyntax)
            {
                return currentToken.Parent is ExternAliasDirectiveSyntax ? 1 : 2;
            }
            else
            {
                return 1;
            }
        }

        private bool IsWhitespace(SyntaxTrivia trivia)
        {
            return trivia.CSharpKind() == SyntaxKind.WhitespaceTrivia
                || trivia.CSharpKind() == SyntaxKind.EndOfLineTrivia;
        }

        private int GetNumberOfLines(SyntaxTriviaList triviaList)
        {
            return triviaList.Sum(t => t.ToFullString().Replace("\r\n", "\r").Count(c => SyntaxFacts.IsNewLine(c)));
        }
    }
}
