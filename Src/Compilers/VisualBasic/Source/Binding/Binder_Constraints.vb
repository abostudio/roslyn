﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Namespace Microsoft.CodeAnalysis.VisualBasic

    Partial Friend Class Binder
        Friend Function BindTypeParameterConstraintClause(
            containingSymbol As Symbol,
            clause As TypeParameterConstraintClauseSyntax,
            diagnostics As DiagnosticBag
        ) As ImmutableArray(Of TypeParameterConstraint)
            Debug.Assert((containingSymbol.Kind = SymbolKind.NamedType) OrElse (containingSymbol.Kind = SymbolKind.Method))

            If clause Is Nothing Then
                Return ImmutableArray(Of TypeParameterConstraint).Empty
            End If

            Dim constraints = TypeParameterConstraintKind.None
            Dim constraintsBuilder = ArrayBuilder(Of TypeParameterConstraint).GetInstance()
            Select Case clause.Kind
                Case SyntaxKind.TypeParameterSingleConstraintClause
                    BindTypeParameterConstraint(containingSymbol, DirectCast(clause, TypeParameterSingleConstraintClauseSyntax).Constraint, constraints, constraintsBuilder, diagnostics)
                Case SyntaxKind.TypeParameterMultipleConstraintClause
                    For Each syntax As ConstraintSyntax In DirectCast(clause, TypeParameterMultipleConstraintClauseSyntax).Constraints
                        BindTypeParameterConstraint(containingSymbol, syntax, constraints, constraintsBuilder, diagnostics)
                    Next
                Case Else
                    Debug.Fail("Unexpected constraint clause: " & clause.Kind)
            End Select
            Return constraintsBuilder.ToImmutableAndFree()
        End Function

        Private Sub BindTypeParameterConstraint(
            containingSymbol As Symbol,
            syntax As ConstraintSyntax,
            ByRef constraints As TypeParameterConstraintKind,
            constraintsBuilder As ArrayBuilder(Of TypeParameterConstraint),
            diagnostics As DiagnosticBag
        )
            Debug.Assert((containingSymbol.Kind = SymbolKind.NamedType) OrElse (containingSymbol.Kind = SymbolKind.Method))

            Select Case syntax.Kind
                Case SyntaxKind.NewConstraint
                    If (constraints And TypeParameterConstraintKind.Constructor) <> 0 Then
                        ReportDiagnostic(diagnostics, syntax, ERRID.ERR_MultipleNewConstraints)
                    ElseIf (constraints And TypeParameterConstraintKind.ValueType) <> 0 Then
                        ReportDiagnostic(diagnostics, syntax, ERRID.ERR_NewAndValueConstraintsCombined)
                    Else
                        constraints = constraints Or TypeParameterConstraintKind.Constructor
                        constraintsBuilder.Add(New TypeParameterConstraint(TypeParameterConstraintKind.Constructor, syntax.GetLocation()))
                    End If

                Case SyntaxKind.ClassConstraint
                    If (constraints And TypeParameterConstraintKind.ReferenceType) <> 0 Then
                        ReportDiagnostic(diagnostics, syntax, ERRID.ERR_MultipleReferenceConstraints)
                    ElseIf (constraints And TypeParameterConstraintKind.ValueType) <> 0 Then
                        ReportDiagnostic(diagnostics, syntax, ERRID.ERR_RefAndValueConstraintsCombined)
                    Else
                        constraints = constraints Or TypeParameterConstraintKind.ReferenceType
                        constraintsBuilder.Add(New TypeParameterConstraint(TypeParameterConstraintKind.ReferenceType, syntax.GetLocation()))
                    End If

                Case SyntaxKind.StructureConstraint
                    If (constraints And TypeParameterConstraintKind.ValueType) <> 0 Then
                        ReportDiagnostic(diagnostics, syntax, ERRID.ERR_MultipleValueConstraints)
                    ElseIf (constraints And TypeParameterConstraintKind.Constructor) <> 0 Then
                        ReportDiagnostic(diagnostics, syntax, ERRID.ERR_NewAndValueConstraintsCombined)
                    ElseIf (constraints And TypeParameterConstraintKind.ReferenceType) <> 0 Then
                        ReportDiagnostic(diagnostics, syntax, ERRID.ERR_RefAndValueConstraintsCombined)
                    Else
                        constraints = constraints Or TypeParameterConstraintKind.ValueType
                        constraintsBuilder.Add(New TypeParameterConstraint(TypeParameterConstraintKind.ValueType, syntax.GetLocation()))
                    End If

                Case SyntaxKind.TypeConstraint
                    Dim typeOrAlias = BindTypeOrAliasSyntax(DirectCast(syntax, TypeConstraintSyntax).Type, diagnostics)
                    Debug.Assert(typeOrAlias IsNot Nothing)
                    Dim constraintType = TryCast(typeOrAlias.UnwrapAlias(), TypeSymbol)
                    If constraintType Is Nothing Then
                        ReportDiagnostic(diagnostics, syntax, ERRID.ERR_UnrecognizedType)
                    Else
                        constraintsBuilder.Add(New TypeParameterConstraint(constraintType, syntax.GetLocation()))

                        AccessCheck.VerifyAccessExposureForMemberType(containingSymbol, syntax, constraintType, diagnostics)
                    End If

                Case Else
                    Debug.Fail("Unexpected constraint: " & syntax.Kind)

            End Select
        End Sub
    End Class

End Namespace
