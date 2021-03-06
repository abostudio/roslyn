﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
'-----------------------------------------------------------------------------
' Contains the definition of the BlockContext
'-----------------------------------------------------------------------------

Namespace Microsoft.CodeAnalysis.VisualBasic.Syntax.InternalSyntax

    Friend NotInheritable Class IfPartContext
        Inherits ExecutableStatementContext

        Friend Sub New(kind As SyntaxKind, statement As StatementSyntax, prevContext As BlockContext)
            MyBase.New(kind, statement, prevContext)

            Debug.Assert(kind = SyntaxKind.ElseIfPart OrElse kind = SyntaxKind.ElsePart)
        End Sub

        Friend Overrides Function ProcessSyntax(node As VisualBasicSyntaxNode) As BlockContext

            Select Case node.Kind
                Case SyntaxKind.ElseIfStatement, SyntaxKind.ElseStatement
                    If BlockKind = SyntaxKind.ElseIfPart Then
                        Dim context = PrevBlock.ProcessSyntax(CreateBlockSyntax(Nothing))
                        Debug.Assert(context Is PrevBlock)
                        Return context.ProcessSyntax(node)
                    End If
            End Select

            Return MyBase.ProcessSyntax(node)
        End Function

        Friend Overrides Function TryLinkSyntax(node As VisualBasicSyntaxNode, ByRef newContext As BlockContext) As LinkResult
            newContext = Nothing
            Select Case node.Kind

                Case _
                   SyntaxKind.ElseIfStatement,
                   SyntaxKind.ElseStatement
                    Return UseSyntax(node, newContext)

                Case Else
                    Return MyBase.TryLinkSyntax(node, newContext)
            End Select
        End Function

        Friend Overrides Function CreateBlockSyntax(statement As StatementSyntax) As VisualBasicSyntaxNode
            Debug.Assert(statement Is Nothing)

            Debug.Assert(BeginStatement IsNot Nothing)

            Dim result As VisualBasicSyntaxNode
            If BeginStatement.Kind = SyntaxKind.ElseStatement Then
                result = SyntaxFactory.ElsePart(DirectCast(BeginStatement, ElseStatementSyntax), Body())
            Else
                result = SyntaxFactory.ElseIfPart(DirectCast(BeginStatement, IfStatementSyntax), Body())
            End If

            FreeStatements()

            Return result
        End Function

        Friend Overrides Function EndBlock(statement As StatementSyntax) As BlockContext
            Dim blockSyntax = CreateBlockSyntax(Nothing)
            Dim context = PrevBlock.ProcessSyntax(blockSyntax)
            Debug.Assert(context Is PrevBlock)

            Return context.EndBlock(statement)
        End Function

        Friend Overrides Function ResyncAndProcessStatementTerminator(statement As StatementSyntax, lambdaContext As BlockContext) As BlockContext
            If statement.Kind = SyntaxKind.ElseStatement Then
                If Not SyntaxFacts.IsTerminator(Parser.CurrentToken.Kind) Then
                    ' Dev10 Else allows a statement to follow on the same line without a colon.
                    ' The colon is missing but this is not a syntax error. However we should
                    ' not allow a label to start after the Else statement
                    Parser.ConsumedStatementTerminator(allowLeadingMultilineTrivia:=False)
                    Return Me
                End If
            End If

            Return MyBase.ResyncAndProcessStatementTerminator(statement, lambdaContext)
        End Function

    End Class

End Namespace