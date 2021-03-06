﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Threading
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Formatting
Imports Microsoft.CodeAnalysis.Options
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.VisualBasic

Namespace Microsoft.CodeAnalysis.VisualBasic.Formatting
    Partial Friend Class TriviaDataFactory
        ''' <summary>   
        ''' represents a general trivia between two tokens. slightly more expensive than others since it
        ''' needs to calculate stuff unlike other cases
        ''' </summary>
        Private Class ComplexTrivia
            Inherits AbstractComplexTrivia(Of SyntaxToken, SyntaxTrivia)

            Public Sub New(optionSet As OptionSet, treeInfo As TreeData, token1 As SyntaxToken, token2 As SyntaxToken)
                MyBase.New(optionSet, treeInfo, token1, token2)
                Contract.ThrowIfNull(treeInfo)
            End Sub

            Protected Overrides Sub ExtractLineAndSpace(text As String, ByRef lines As Integer, ByRef spaces As Integer)
                text.ProcessTextBetweenTokens(Me.TreeInfo, Me.Token1, Me.OptionSet.GetOption(FormattingOptions.TabSize, LanguageNames.VisualBasic), lines, spaces)
            End Sub

            Protected Overrides Function ConvertToken(token As SyntaxToken) As SyntaxToken
                Return token
            End Function

            Protected Overrides Function ConvertTrivia(trivia As SyntaxTrivia) As SyntaxTrivia
                Return trivia
            End Function

            Protected Overrides Function CreateComplexTrivia(line As Integer, space As Integer) As TriviaData
                Return New ModifiedComplexTrivia(Me.OptionSet, Me, line, space)
            End Function

            Protected Overrides Function Format(context As FormattingContext,
                                                formattingRules As ChainedFormattingRules,
                                                lines As Integer,
                                                spaces As Integer,
                                                cancellationToken As CancellationToken) As TriviaDataWithList(Of SyntaxTrivia)
                Return New FormattedComplexTrivia(context, formattingRules, Me.Token1, Me.Token2, lines, spaces, Me.OriginalString, cancellationToken)
            End Function

            Protected Overrides Function ContainsSkippedTokensOrText(list As TriviaList) As Boolean
                Return CodeShapeAnalyzer.ContainsSkippedTokensOrText(list)
            End Function

            Private Function ShouldFormat(context As FormattingContext) As Boolean
                Dim commonToken1 As SyntaxToken = Me.Token1
                Dim commonToken2 As SyntaxToken = Me.Token2

                Dim list As TriviaList = New TriviaList(commonToken1.TrailingTrivia, commonToken2.LeadingTrivia)
                Contract.ThrowIfFalse(list.Count > 0)

                ' okay, now, check whether we need or are able to format noisy tokens
                If ContainsSkippedTokensOrText(list) Then
                    Return False
                End If

                Dim beginningOfNewLine = Me.Token1.VisualBasicKind = SyntaxKind.None

                If Not Me.SecondTokenIsFirstTokenOnLine AndAlso Not beginningOfNewLine Then
                    Return CodeShapeAnalyzer.ShouldFormatSingleLine(list)
                End If

                Debug.Assert(Me.SecondTokenIsFirstTokenOnLine OrElse beginningOfNewLine)

                Return CodeShapeAnalyzer.ShouldFormatMultiLine(context, beginningOfNewLine, list)
            End Function

            Public Overrides Sub Format(context As FormattingContext,
                                        formattingRules As ChainedFormattingRules,
                                        formattingResultApplier As Action(Of Integer, TriviaData),
                                        cancellationToken As CancellationToken,
                                        Optional tokenPairIndex As Integer = TokenPairIndexNotNeeded)
                If Not ShouldFormat(context) Then
                    Return
                End If

                formattingResultApplier(tokenPairIndex, Format(context, formattingRules, Me.LineBreaks, Me.Spaces, cancellationToken))
            End Sub

            Public Overrides Function GetTextChanges(span As TextSpan) As IEnumerable(Of TextChange)
                Throw New NotImplementedException()
            End Function

            Public Overrides Function GetTriviaList(cancellationToken As Threading.CancellationToken) As List(Of SyntaxTrivia)
                Throw New NotImplementedException()
            End Function
        End Class
    End Class
End Namespace