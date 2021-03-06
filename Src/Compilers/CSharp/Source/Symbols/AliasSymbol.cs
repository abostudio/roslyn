﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Symbol representing a using alias appearing in a compilation unit or within a namespace
    /// declaration. Generally speaking, these symbols do not appear in the set of symbols reachable
    /// from the unnamed namespace declaration.  In other words, when a using alias is used in a
    /// program, it acts as a transparent alias, and the symbol to which it is an alias is used in
    /// the symbol table.  For example, in the source code
    /// <pre>
    /// namespace NS
    /// {
    ///     using o = System.Object;
    ///     partial class C : o {}
    ///     partial class C : object {}
    ///     partial class C : System.Object {}
    /// }
    /// </pre>
    /// all three declarations for class C are equivalent and result in the same symbol table object
    /// for C. However, these using alias symbols do appear in the results of certain SemanticModel
    /// APIs. Specifically, for the base clause of the first of C's class declarations, the
    /// following APIs may produce a result that contains an AliasSymbol:
    /// <pre>
    ///     SemanticInfo SemanticModel.GetSemanticInfo(ExpressionSyntax expression);
    ///     SemanticInfo SemanticModel.BindExpression(CSharpSyntaxNode location, ExpressionSyntax expression);
    ///     SemanticInfo SemanticModel.BindType(CSharpSyntaxNode location, ExpressionSyntax type);
    ///     SemanticInfo SemanticModel.BindNamespaceOrType(CSharpSyntaxNode location, ExpressionSyntax type);
    /// </pre>
    /// Also, the following are affected if container==null (and, for the latter, when arity==null
    /// or arity==0):
    /// <pre>
    ///     IList&lt;string&gt; SemanticModel.LookupNames(CSharpSyntaxNode location, NamespaceOrTypeSymbol container = null, LookupOptions options = LookupOptions.Default, List&lt;string> result = null);
    ///     IList&lt;Symbol&gt; SemanticModel.LookupSymbols(CSharpSyntaxNode location, NamespaceOrTypeSymbol container = null, string name = null, int? arity = null, LookupOptions options = LookupOptions.Default, List&lt;Symbol> results = null);
    /// </pre>
    /// </summary>
    internal sealed class AliasSymbol : Symbol, IAliasSymbol
    {
        private readonly SyntaxToken aliasName;
        private readonly InContainerBinder binder;

        private SymbolCompletionState state;
        private NamespaceOrTypeSymbol aliasTarget;
        private readonly ImmutableArray<Location> locations;  // NOTE: can be empty for the "global" alias.

        // lazy binding
        private NameSyntax aliasTargetName;
        private readonly bool isExtern;
        private ImmutableArray<Diagnostic> aliasTargetDiagnostics;

        private AliasSymbol(InContainerBinder binder, NamespaceOrTypeSymbol target, SyntaxToken aliasName, ImmutableArray<Location> locations)
        {
            this.aliasName = aliasName;
            this.locations = locations;
            this.aliasTarget = target;
            this.binder = binder;
            this.state.NotePartComplete(CompletionPart.AliasTarget);
        }

        private AliasSymbol(InContainerBinder binder, SyntaxToken aliasName)
        {
            this.aliasName = aliasName;
            this.locations = ImmutableArray.Create(aliasName.GetLocation());
            this.binder = binder;
        }

        internal AliasSymbol(InContainerBinder binder, UsingDirectiveSyntax syntax)
            : this(binder, syntax.Alias.Name.Identifier)
        {
            this.aliasTargetName = syntax.Name;
        }

        internal AliasSymbol(InContainerBinder binder, ExternAliasDirectiveSyntax syntax)
            : this(binder, syntax.Identifier)
        {
            this.isExtern = true;
        }

        // For the purposes of SemanticModel, it is convenient to have an AliasSymbol for the "global" namespace that "global::" binds
        // to. This alias symbol is returned only when binding "global::" (special case code).
        internal static AliasSymbol CreateGlobalNamespaceAlias(NamespaceSymbol globalNamespace, InContainerBinder globalNamespaceBinder)
        {
            SyntaxToken aliasName = SyntaxFactory.Identifier(SyntaxFactory.TriviaList(), SyntaxKind.GlobalKeyword, "global", "global", SyntaxFactory.TriviaList());
            return new AliasSymbol(globalNamespaceBinder, globalNamespace, aliasName, ImmutableArray<Location>.Empty);
        }

        internal static AliasSymbol CreateCustomDebugInfoAlias(NamespaceOrTypeSymbol targetSymbol, SyntaxToken aliasToken, InContainerBinder binder)
        {
            return new AliasSymbol(binder, targetSymbol, aliasToken, ImmutableArray.Create(aliasToken.GetLocation()));
        }

        public override string Name
        {
            get
            {
                return aliasName.ValueText;
            }
        }

        public override SymbolKind Kind
        {
            get
            {
                return SymbolKind.Alias;
            }
        }

        /// <summary>
        /// Gets the <see cref="NamespaceOrTypeSymbol"/> for the
        /// namespace or type referenced by the alias.
        /// </summary>
        public NamespaceOrTypeSymbol Target
        {
            get
            {
                return GetAliasTarget(basesBeingResolved: null);
            }
        }

        internal override LexicalSortKey GetLexicalSortKey()
        {
            return (locations.Length > 0) ? new LexicalSortKey(locations[0], binder.Compilation) : LexicalSortKey.NotInSource;
        }

        public override ImmutableArray<Location> Locations
        {
            get
            {
                return locations;
            }
        }

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                return GetDeclaringSyntaxReferenceHelper<UsingDirectiveSyntax>(locations);
            }
        }

        public override bool IsExtern
        {
            get
            {
                return this.isExtern;
            }
        }

        public override bool IsSealed
        {
            get
            {
                return false;
            }
        }

        public override bool IsAbstract
        {
            get
            {
                return false;
            }
        }
        public override bool IsOverride
        {
            get
            {
                return false;
            }
        }

        public override bool IsVirtual
        {
            get
            {
                return false;
            }
        }

        public override bool IsStatic
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Returns data decoded from Obsolete attribute or null if there is no Obsolete attribute.
        /// This property returns ObsoleteAttributeData.Uninitialized if attribute arguments haven't been decoded yet.
        /// </summary>
        internal sealed override ObsoleteAttributeData ObsoleteAttributeData
        {
            get { return null; }
        }

        public override Accessibility DeclaredAccessibility
        {
            get
            {
                return Accessibility.NotApplicable;
            }
        }

        /// <summary>
        /// Using aliases in C# are always contained within a namespace declaration, or at the top
        /// level within a compilation unit, within the implicit unnamed namespace declaration.  We
        /// return that as the "containing" symbol, even though the alias isn't a member of the
        /// namespace as such.
        /// </summary>
        public override Symbol ContainingSymbol
        {
            get
            {
                return binder.ContainingMemberOrLambda;
            }
        }

        internal override TResult Accept<TArg, TResult>(CSharpSymbolVisitor<TArg, TResult> visitor, TArg a)
        {
            return visitor.VisitAlias(this, a);
        }

        public override void Accept(CSharpSymbolVisitor visitor)
        {
            visitor.VisitAlias(this);
        }

        public override TResult Accept<TResult>(CSharpSymbolVisitor<TResult> visitor)
        {
            return visitor.VisitAlias(this);
        }

        // basesBeingResolved is only used to break circular references.
        internal NamespaceOrTypeSymbol GetAliasTarget(ConsList<Symbol> basesBeingResolved)
        {
            if (!state.HasComplete(CompletionPart.AliasTarget))
            {
                // the target is not yet bound. If it is an ordinary alias, bind the target
                // symbol. If it is an extern alias then find the target in the list of metadata references.
                var newDiagnostics = DiagnosticBag.GetInstance();

                NamespaceOrTypeSymbol symbol = this.IsExtern ?
                    ResolveExternAliasTarget(newDiagnostics) :
                    ResolveAliasTarget(this.binder, this.aliasTargetName, newDiagnostics, basesBeingResolved);

                if ((object)Interlocked.CompareExchange(ref this.aliasTarget, symbol, null) == null)
                {
                    bool won = ImmutableInterlocked.InterlockedInitialize(ref this.aliasTargetDiagnostics, newDiagnostics.ToReadOnlyAndFree());
                    Debug.Assert(won, "Only one thread can win the alias target CompareExchange");

                    state.NotePartComplete(CompletionPart.AliasTarget);
                    // we do not clear this.aliasTargetName, as another thread might be about to use it for ResolveAliasTarget(...)
                }
                else
                {
                    newDiagnostics.Free();
                    // Wait for diagnostics to have been reported if another thread resolves the alias
                    state.SpinWaitComplete(CompletionPart.AliasTarget, default(CancellationToken));
                }

            }

            return aliasTarget;
        }

        internal ImmutableArray<Diagnostic> AliasTargetDiagnostics
        {
            get
            {
                GetAliasTarget(null);
                Debug.Assert(!this.aliasTargetDiagnostics.IsDefault);
                return this.aliasTargetDiagnostics;
            }
        }

        internal void CheckConstraints(DiagnosticBag diagnostics)
        {
            var target = this.Target as TypeSymbol;
            if ((object)target != null && this.locations.Length > 0)
            {
                var corLibrary = this.ContainingAssembly.CorLibrary;
                var conversions = new TypeConversions(corLibrary);
                target.CheckAllConstraints(conversions, this.locations[0], diagnostics);
            }
        }

        private NamespaceSymbol ResolveExternAliasTarget(DiagnosticBag diagnostics)
        {
            NamespaceSymbol target;
            if (!this.binder.Compilation.GetExternAliasTarget(aliasName.ValueText, out target))
            {
                diagnostics.Add(ErrorCode.ERR_BadExternAlias, aliasName.GetLocation(), aliasName.ValueText);
            }

            Debug.Assert((object)target != null);

            return target;
        }

        private static NamespaceOrTypeSymbol ResolveAliasTarget(InContainerBinder binder, NameSyntax syntax, DiagnosticBag diagnostics, ConsList<Symbol> basesBeingResolved)
        {
            var declarationBinder = binder.WithAdditionalFlags(BinderFlags.SuppressConstraintChecks | BinderFlags.SuppressObsoleteChecks);
            return declarationBinder.BindNamespaceOrTypeSymbol(syntax, diagnostics, basesBeingResolved);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (ReferenceEquals(obj, null))
            {
                return false;
            }

            AliasSymbol other = obj as AliasSymbol;

            return (object)other != null &&
                Equals(this.Locations.FirstOrDefault(), other.Locations.FirstOrDefault()) &&
                this.DeclaringCompilation == other.DeclaringCompilation;
        }

        public override int GetHashCode()
        {
            if (this.Locations.Length > 0)
                return this.Locations.First().GetHashCode();
            else
                return Name.GetHashCode();
        }

        internal override bool RequiresCompletion
        {
            get { return true; }
        }

        #region IAliasSymbol Members

        INamespaceOrTypeSymbol IAliasSymbol.Target
        {
            get { return this.Target; }
        }

        #endregion

        #region ISymbol Members

        public override void Accept(SymbolVisitor visitor)
        {
            visitor.VisitAlias(this);
        }

        public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
        {
            return visitor.VisitAlias(this);
        }

        #endregion
    }
}
