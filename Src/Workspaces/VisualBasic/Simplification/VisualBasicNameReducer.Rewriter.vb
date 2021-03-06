﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Threading
Imports Microsoft.CodeAnalysis.Options
Imports Microsoft.CodeAnalysis.Simplification
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Simplification
    Partial Friend Class VisualBasicNameReducer
        Private Class Rewriter
            Inherits AbstractExpressionRewriter

            Public Sub New(optionSet As OptionSet, cancellationToken As CancellationToken)
                MyBase.New(optionSet, cancellationToken)
            End Sub

            Public Overrides Function VisitGenericName(node As GenericNameSyntax) As SyntaxNode
                Dim oldAlwaysSimplify = Me._alwaysSimplify
                If Not Me._alwaysSimplify Then
                    Me._alwaysSimplify = node.HasAnnotation(Simplifier.Annotation)
                End If

                Dim result = SimplifyExpression(
                    node,
                    newNode:=MyBase.VisitGenericName(node),
                    simplifier:=AddressOf SimplifyName)

                Me._alwaysSimplify = oldAlwaysSimplify

                Return result
            End Function

            Public Overrides Function VisitIdentifierName(node As IdentifierNameSyntax) As SyntaxNode
                Dim oldAlwaysSimplify = Me._alwaysSimplify
                If Not Me._alwaysSimplify Then
                    Me._alwaysSimplify = node.HasAnnotation(Simplifier.Annotation)
                End If

                Dim result = SimplifyExpression(
                    node,
                    newNode:=MyBase.VisitIdentifierName(node),
                    simplifier:=AddressOf SimplifyName)

                Me._alwaysSimplify = oldAlwaysSimplify

                Return result
            End Function

            Public Overrides Function VisitQualifiedName(node As QualifiedNameSyntax) As SyntaxNode
                Dim oldAlwaysSimplify = Me._alwaysSimplify
                If Not Me._alwaysSimplify Then
                    Me._alwaysSimplify = node.HasAnnotation(Simplifier.Annotation)
                End If

                Dim result = SimplifyExpression(
                    node,
                    newNode:=MyBase.VisitQualifiedName(node),
                    simplifier:=AddressOf SimplifyName)

                Me._alwaysSimplify = oldAlwaysSimplify

                Return result
            End Function

            Public Overrides Function VisitMemberAccessExpression(node As MemberAccessExpressionSyntax) As SyntaxNode
                Dim oldAlwaysSimplify = Me._alwaysSimplify
                If Not Me._alwaysSimplify Then
                    Me._alwaysSimplify = node.HasAnnotation(Simplifier.Annotation)
                End If

                Dim result = SimplifyExpression(
                    node,
                    newNode:=MyBase.VisitMemberAccessExpression(node),
                    simplifier:=AddressOf SimplifyName)

                Me._alwaysSimplify = oldAlwaysSimplify

                Return result
            End Function

            Public Overrides Function VisitNullableType(node As NullableTypeSyntax) As SyntaxNode
                Dim oldAlwaysSimplify = Me._alwaysSimplify
                If Not Me._alwaysSimplify Then
                    Me._alwaysSimplify = node.HasAnnotation(Simplifier.Annotation)
                End If

                Dim result = SimplifyExpression(
                    node,
                    newNode:=MyBase.VisitNullableType(node),
                    simplifier:=AddressOf SimplifyName)

                Me._alwaysSimplify = oldAlwaysSimplify

                Return result
            End Function

        End Class
    End Class
End Namespace
