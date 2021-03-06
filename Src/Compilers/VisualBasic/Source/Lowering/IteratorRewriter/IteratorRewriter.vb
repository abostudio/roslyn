﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Runtime.CompilerServices
Imports System.Threading
Imports Microsoft.Cci
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Collections
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic

    Partial Friend NotInheritable Class IteratorRewriter
        Inherits StateMachineRewriter(Of IteratorStateMachineTypeSymbol, FieldSymbol)

        Private ReadOnly elementType As TypeSymbol
        Private ReadOnly isEnumerable As Boolean
        Private ReadOnly iteratorClass As IteratorStateMachineTypeSymbol

        Private currentField As FieldSymbol
        Private initialThreadIdField As FieldSymbol

        Public Sub New(body As BoundStatement,
               method As MethodSymbol,
               isEnumerable As Boolean,
               iteratorClass As IteratorStateMachineTypeSymbol,
               compilationState As TypeCompilationState,
               diagnostics As DiagnosticBag,
               generateDebugInfo As Boolean)

            MyBase.New(body, method, compilationState, diagnostics, generateDebugInfo)

            Me.isEnumerable = isEnumerable
            Me.iteratorClass = iteratorClass

            Dim methodReturnType = method.ReturnType
            If methodReturnType.GetArity = 0 Then
                Me.elementType = method.ContainingAssembly.GetSpecialType(SpecialType.System_Object)
            Else
                ' the element type may contain method type parameters, which are now alpha-renamed into type parameters of the generated class
                Me.elementType = DirectCast(methodReturnType, NamedTypeSymbol).TypeArgumentsNoUseSiteDiagnostics().Single().InternalSubstituteTypeParameters(Me.TypeMap)
            End If
        End Sub

        ''' <summary>
        ''' Rewrite an iterator method into a state machine class.
        ''' </summary>
        Friend Overloads Shared Function Rewrite(body As BoundBlock,
                                                 method As MethodSymbol,
                                                 compilationState As TypeCompilationState,
                                                 diagnostics As DiagnosticBag,
                                                 generateDebugInfo As Boolean) As BoundBlock

            If body.HasErrors Or Not method.IsIterator Then
                Return body
            End If

            Dim methodReturnType As TypeSymbol = method.ReturnType

            Dim retSpecialType = method.ReturnType.OriginalDefinition.SpecialType
            Dim isEnumerable As Boolean = retSpecialType = SpecialType.System_Collections_Generic_IEnumerable_T OrElse
                                          retSpecialType = SpecialType.System_Collections_IEnumerable

            Dim elementType As TypeSymbol
            If method.ReturnType.IsDefinition Then
                elementType = method.ContainingAssembly.GetSpecialType(SpecialType.System_Object)
            Else
                elementType = DirectCast(methodReturnType, NamedTypeSymbol).TypeArgumentsNoUseSiteDiagnostics(0)
            End If

            Dim iteratorClass = New IteratorStateMachineTypeSymbol(method, compilationState.GenerateTempNumber(), elementType, isEnumerable)

            Dim rewriter As New IteratorRewriter(body, method, isEnumerable, iteratorClass, compilationState, diagnostics, generateDebugInfo)

            ' check if we have all the types we need
            If rewriter.EnsureAllSymbolsAndSignature() Then
                Return body
            End If

            Return rewriter.Rewrite()
        End Function

        Friend Overrides Function EnsureAllSymbolsAndSignature() As Boolean
            Dim hasErrors As Boolean = MyBase.EnsureAllSymbolsAndSignature

            If Me.Method.IsSub OrElse Me.elementType.IsErrorType Then
                hasErrors = True
            End If

            ' NOTE: in current implementation these attributes must exist
            ' TODO: change to "don't use if not found"
            EnsureWellKnownMember(Of MethodSymbol)(WellKnownMember.System_Runtime_CompilerServices_CompilerGeneratedAttribute__ctor, hasErrors)
            EnsureWellKnownMember(Of MethodSymbol)(WellKnownMember.System_Diagnostics_DebuggerNonUserCodeAttribute__ctor, hasErrors)

            ' NOTE: We don't ensure DebuggerStepThroughAttribute, it is just not emitted if not found
            ' EnsureWellKnownMember(Of MethodSymbol)(WellKnownMember.System_Diagnostics_DebuggerStepThroughAttribute__ctor, hasErrors)

            ' TODO: do we need these here? They are used on the actual iterator method.
            ' EnsureWellKnownMember(Of MethodSymbol)(WellKnownMember.System_Runtime_CompilerServices_IteratorStateMachine__ctor, hasErrors)

            EnsureSpecialType(SpecialType.System_Object, hasErrors)
            EnsureSpecialType(SpecialType.System_Boolean, hasErrors)
            EnsureSpecialType(SpecialType.System_Int32, hasErrors)

            If Me.Method.ReturnType.IsDefinition Then
                If Me.isEnumerable Then
                    EnsureSpecialType(SpecialType.System_Collections_IEnumerator, hasErrors)
                End If
            Else
                If Me.isEnumerable Then
                    EnsureSpecialType(SpecialType.System_Collections_Generic_IEnumerator_T, hasErrors)
                    EnsureSpecialType(SpecialType.System_Collections_IEnumerable, hasErrors)
                End If

                EnsureSpecialType(SpecialType.System_Collections_IEnumerator, hasErrors)
            End If

            EnsureSpecialType(SpecialType.System_IDisposable, hasErrors)

            Return hasErrors
        End Function

        Protected Overrides Sub GenerateFields()
            ' Add a field: T current
            currentField = F.SynthesizeField(elementType, Me.Method, GeneratedNames.MakeIteratorCurrentFieldName(), Accessibility.Friend)

            ' if it is an Enumerable, add a field: initialThreadId As Integer
            initialThreadIdField = If(isEnumerable,
                F.SynthesizeField(F.SpecialType(SpecialType.System_Int32), Me.Method, GeneratedNames.MakeIteratorInitialThreadIdName()),
                Nothing)

        End Sub

        Protected Overrides Sub GenerateMethodImplementations()
            Dim managedThreadId As BoundExpression = Nothing  ' Thread.CurrentThread.ManagedThreadId

            ' Add bool IEnumerator.MoveNext() and void IDisposable.Dispose()
            Dim disposeMethod = Me.StartMethodImplementation(SpecialMember.System_IDisposable__Dispose,
                                                             "Dispose",
                                                             DebugAttributes.DebuggerNonUserCodeAttribute,
                                                             Accessibility.Private,
                                                             False)

            Dim moveNextMethod = Me.StartMethodImplementation(SpecialMember.System_Collections_IEnumerator__MoveNext,
                                                             "MoveNext",
                                                             DebugAttributes.CompilerGeneratedAttribute,
                                                             Accessibility.Private,
                                                             True)

            GenerateMoveNextAndDispose(moveNextMethod, disposeMethod)
            F.CurrentMethod = moveNextMethod

            If isEnumerable Then
                ' generate the code for GetEnumerator()
                '    IEnumerable<elementType> result;
                '    if (this.initialThreadId == Thread.CurrentThread.ManagedThreadId && this.state == -2)
                '    {
                '        this.state = 0;
                '        result = this;
                '    }
                '    else
                '    {
                '        result = new Ints0_Impl(0);
                '    }
                '    result.parameter = this.parameterProxy; ' copy all of the parameter proxies

                ' Add IEnumerator<int> IEnumerable<int>.GetEnumerator()
                Dim getEnumeratorGeneric = Me.StartMethodImplementation(F.SpecialType(SpecialType.System_Collections_Generic_IEnumerable_T).Construct(elementType),
                                                            SpecialMember.System_Collections_Generic_IEnumerable_T__GetEnumerator,
                                                            "GetEnumerator",
                                                            DebugAttributes.DebuggerNonUserCodeAttribute,
                                                            Accessibility.Private,
                                                            False)

                Dim bodyBuilder = ArrayBuilder(Of BoundStatement).GetInstance()
                Dim resultVariable = F.SynthesizedLocal(StateMachineClass)      ' iteratorClass result;

                Dim currentManagedThreadIdMethod As MethodSymbol = Nothing

                Dim currentManagedThreadIdProperty As PropertySymbol = F.WellKnownMember(Of PropertySymbol)(WellKnownMember.System_Environment__CurrentManagedThreadId, isOptional:=True)

                If (currentManagedThreadIdProperty IsNot Nothing) Then
                    currentManagedThreadIdMethod = currentManagedThreadIdProperty.GetMethod()
                End If

                If (currentManagedThreadIdMethod IsNot Nothing) Then
                    managedThreadId = F.Call(Nothing, currentManagedThreadIdMethod)
                Else
                    managedThreadId = F.Property(F.Property(WellKnownMember.System_Threading_Thread__CurrentThread), WellKnownMember.System_Threading_Thread__ManagedThreadId)
                End If

                ' if (this.state == -2 && this.initialThreadId == Thread.CurrentThread.ManagedThreadId)
                '    this.state = 0;
                '    result = this;
                '    goto thisInitialized
                ' else
                '    result = new IteratorClass(0)
                '    ' initialize [Me] if needed
                '    thisInitialized:
                '    ' initialize other fields
                Dim thisInitialized = F.GenerateLabel("thisInitialized")
                bodyBuilder.Add(
                    F.If(
                    condition:=
                        F.LogicalAndAlso(
                            F.IntEqual(F.Field(F.[Me](), StateField, False), F.Literal(StateMachineStates.FinishedStateMachine)),
                            F.IntEqual(F.Field(F.[Me](), initialThreadIdField, False), managedThreadId)),
                    thenClause:=
                        F.Block(
                            F.Assignment(F.Field(F.[Me](), StateField, True), F.Literal(StateMachineStates.FirstUnusedState)),
                            F.Assignment(F.Local(resultVariable, True), F.[Me]()),
                            If(Method.IsShared OrElse Method.MeParameter.Type.IsReferenceType,
                                    F.Goto(thisInitialized),
                                    DirectCast(F.Block(), BoundStatement))
                        ),
                    elseClause:=
                        F.Assignment(F.Local(resultVariable, True), F.[New](StateMachineClass.Constructor, F.Literal(0)))
                    ))

                ' Initialize all the parameter copies
                Dim copySrc = InitialParameters
                Dim copyDest = LocalProxies
                If Not Method.IsShared Then
                    ' starting with "this"
                    Dim proxy As FieldSymbol = Nothing
                    If (copyDest.TryGetValue(Method.MeParameter, proxy)) Then
                        bodyBuilder.Add(
                            F.Assignment(
                                F.Field(F.Local(resultVariable, True), proxy.AsMember(StateMachineClass), True),
                                F.Field(F.[Me](), copySrc(Method.MeParameter).AsMember(F.CurrentType), False)))
                    End If
                End If

                bodyBuilder.Add(F.Label(thisInitialized))

                For Each parameter In Method.Parameters
                    Dim proxy As FieldSymbol = Nothing
                    If (copyDest.TryGetValue(parameter, proxy)) Then
                        bodyBuilder.Add(
                            F.Assignment(
                                F.Field(F.Local(resultVariable, True), proxy.AsMember(StateMachineClass), True),
                                F.Field(F.[Me](), copySrc(parameter).AsMember(F.CurrentType), False)))
                    End If
                Next

                bodyBuilder.Add(F.Return(F.Local(resultVariable, False)))
                F.CloseMethod(F.Block(ImmutableArray.Create(resultVariable), bodyBuilder.ToImmutableAndFree()))

                ' Generate IEnumerable.GetEnumerator
                ' NOTE: this is a private implementing method. Its name is irrelevant
                '       but must be different from another GetEnumerator. Dev11 uses GetEnumerator0 here.
                '       IEnumerable.GetEnumerator seems better -
                '       it is clear why we have the property, and "Current" suffix will be shared in metadata with another Current.
                '       It is also consistent with the naming of IEnumerable.Current (see below).
                Me.StartMethodImplementation(SpecialMember.System_Collections_IEnumerable__GetEnumerator,
                                            "IEnumerable.GetEnumerator",
                                            DebugAttributes.DebuggerNonUserCodeAttribute,
                                            Accessibility.Private,
                                            False)
                F.CloseMethod(F.Return(F.Call(F.[Me](), getEnumeratorGeneric)))
            End If

                ' Add T IEnumerator<T>.Current
                Dim name =
                Me.StartPropertyGetImplementation(F.SpecialType(SpecialType.System_Collections_Generic_IEnumerator_T).Construct(elementType),
                                                                 SpecialMember.System_Collections_Generic_IEnumerator_T__Current,
                                                                 "Current",
                                                                 DebugAttributes.DebuggerNonUserCodeAttribute,
                                                                 Accessibility.Private,
                                                                 False)

                F.CloseMethod(F.Return(F.Field(F.[Me](), currentField, False)))

                ' Add void IEnumerator.Reset()
                Me.StartMethodImplementation(SpecialMember.System_Collections_IEnumerator__Reset,
                                "Reset",
                                DebugAttributes.DebuggerNonUserCodeAttribute,
                                Accessibility.Private,
                                False)
                F.CloseMethod(F.Throw(F.[New](F.WellKnownType(WellKnownType.System_NotSupportedException))))

            ' Add object IEnumerator.Current
            ' NOTE: this is a private implementing property. Its name is irrelevant
            '       but must be different from another Current.
            '       Dev11 uses fully qualified and substituted name here (System.Collections.Generic.IEnumerator(Of Object).Current), 
            '       It may be an overkill and may lead to metadata bloat. 
            '       IEnumerable.Current seems better -
            '       it is clear why we have the property, and "Current" suffix will be shared in metadata with another Current.
            Me.StartPropertyGetImplementation(SpecialMember.System_Collections_IEnumerator__Current,
                                "IEnumerator.Current",
                                DebugAttributes.DebuggerNonUserCodeAttribute,
                                Accessibility.Private,
                                False)
                F.CloseMethod(F.Return(F.Field(F.[Me](), currentField, False)))

                ' Add a body for the constructor
                If True Then
                    F.CurrentMethod = StateMachineClass.Constructor
                    Dim bodyBuilder = ArrayBuilder(Of BoundStatement).GetInstance()
                    bodyBuilder.Add(F.BaseInitialization())
                    bodyBuilder.Add(F.Assignment(F.Field(F.[Me](), StateField, True), F.Parameter(F.CurrentMethod.Parameters(0)).MakeRValue))    ' this.state = state

                    If isEnumerable Then
                        ' this.initialThreadId = Thread.CurrentThread.ManagedThreadId;
                        bodyBuilder.Add(F.Assignment(F.Field(F.[Me](), initialThreadIdField, True), managedThreadId))
                    End If

                    bodyBuilder.Add(F.Return())
                    F.CloseMethod(F.Block(bodyBuilder.ToImmutableAndFree()))
                    bodyBuilder = Nothing
                End If

        End Sub

        Protected Overrides Function GenerateReplacementBody(stateMachineVariable As LocalSymbol, frameType As NamedTypeSymbol) As BoundStatement
            Return F.Return(F.Local(stateMachineVariable, False))
        End Function

        Protected Overrides Sub InitializeStateMachine(bodyBuilder As ArrayBuilder(Of BoundStatement), frameType As NamedTypeSymbol, stateMachineLocal As LocalSymbol)
            ' Dim stateMachineLocal As new IteratorImplementationClass(N)
            ' where N is either 0 (if we're producing an enumerator) or -2 (if we're producing an enumerable)
            Dim initialState = If(isEnumerable, StateMachineStates.FinishedStateMachine, StateMachineStates.FirstUnusedState)
            bodyBuilder.Add(
                F.Assignment(
                    F.Local(stateMachineLocal, True),
                    F.[New](StateMachineClass.Constructor.AsMember(frameType), F.Literal(initialState))))
        End Sub

        Protected Overrides ReadOnly Property PreserveInitialLocals As Boolean
            Get
                Return Me.isEnumerable
            End Get
        End Property

        Protected Overrides ReadOnly Property StateMachineClass As IteratorStateMachineTypeSymbol
            Get
                Return iteratorClass
            End Get
        End Property

        Friend Overrides ReadOnly Property TypeMap As TypeSubstitution
            Get
                Return iteratorClass.TypeSubstitution
            End Get
        End Property

        Private Sub GenerateMoveNextAndDispose(moveNextMethod As SynthesizedImplementationMethod, disposeMethod As SynthesizedImplementationMethod)
            Dim rewriter = New IteratorMethodToClassRewriter(Me.Method, Me.F, Me.StateField, Me.currentField, Me.LocalProxies, Me.Diagnostics, Me.GenerateDebugInfo)

            rewriter.GenerateMoveNextAndDispose(Body, moveNextMethod, disposeMethod)
        End Sub

        Protected Overrides Function CreateByValLocalCapture(field As FieldSymbol, local As LocalSymbol) As FieldSymbol
            Return field
        End Function

        Protected Overrides Function CreateParameterCapture(field As FieldSymbol, parameter As ParameterSymbol) As FieldSymbol
            Return field
        End Function

        Protected Overrides Sub InitializeParameterWithProxy(parameter As ParameterSymbol, proxy As FieldSymbol, stateMachineVariable As LocalSymbol, initializers As ArrayBuilder(Of BoundExpression))
            Debug.Assert(proxy IsNot Nothing)

            Dim frameType As NamedTypeSymbol = If(Me.Method.IsGenericMethod,
                                                  Me.StateMachineClass.Construct(Me.Method.TypeArguments),
                                                  Me.StateMachineClass)

            Dim expression As BoundExpression = If(parameter.IsMe,
                                                   DirectCast(Me.F.[Me](), BoundExpression),
                                                   Me.F.Parameter(parameter).MakeRValue())
            initializers.Add(
                Me.F.AssignmentExpression(
                    Me.F.Field(
                        Me.F.Local(stateMachineVariable, False),
                        proxy.AsMember(frameType),
                        True),
                    expression))
        End Sub
    End Class
End Namespace

