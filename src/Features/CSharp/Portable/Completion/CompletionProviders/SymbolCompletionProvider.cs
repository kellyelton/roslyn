// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Completion.Providers;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Extensions.ContextQuery;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Recommendations;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Extensions.ContextQuery;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis.LanguageServices;
using System.Linq;
using Microsoft.CodeAnalysis.Shared.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Completion.Providers
{
    internal partial class SymbolCompletionProvider : AbstractRecommendationServiceBasedCompletionProvider
    {
        protected override Task<IEnumerable<ISymbol>> GetSymbolsWorker(AbstractSyntaxContext context, int position, OptionSet options, CancellationToken cancellationToken)
        {
            return Recommender.GetRecommendedSymbolsAtPositionAsync(context.SemanticModel, position, context.Workspace, options, cancellationToken);
        }

        protected override bool IsInstrinsic(ISymbol s)
        {
            var ts = s as ITypeSymbol;
            return ts != null && ts.IsIntrinsicType();
        }

        protected override string GetInsertionText(ISymbol symbol, AbstractSyntaxContext context, char ch)
        {
            return GetInsertionText(symbol, context);
        }

        public static string GetInsertionText(ISymbol symbol, AbstractSyntaxContext context)
        {
            string name;

            if (CommonCompletionUtilities.TryRemoveAttributeSuffix(symbol, context.IsAttributeNameContext, context.GetLanguageService<ISyntaxFactsService>(), out name))
            {
                // Cannot escape Attribute name with the suffix removed. Only use the name with
                // the suffix removed if it does not need to be escaped.
                if (name.Equals(name.EscapeIdentifier()))
                {
                    return name;
                }
            }

            return symbol.Name.EscapeIdentifier(isQueryContext: context.IsInQuery);
        }

        internal override bool IsInsertionTrigger(SourceText text, int characterPosition, OptionSet options)
        {
            return CompletionUtilities.IsTriggerCharacter(text, characterPosition, options);
        }

        protected override async Task<bool> IsSemanticTriggerCharacterAsync(Document document, int characterPosition, CancellationToken cancellationToken)
        {
            bool? result = await IsTriggerOnDotAsync(document, characterPosition, cancellationToken).ConfigureAwait(false);
            if (result.HasValue)
            {
                return result.Value;
            }

            return true;
        }

        private async Task<bool?> IsTriggerOnDotAsync(Document document, int characterPosition, CancellationToken cancellationToken)
        {
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (text[characterPosition] != '.')
            {
                return null;
            }

            // don't want to trigger after a number.  All other cases after dot are ok.
            var tree = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var token = tree.FindToken(characterPosition);
            if (token.Kind() == SyntaxKind.DotToken)
            {
                token = token.GetPreviousToken();
            }

            return token.Kind() != SyntaxKind.NumericLiteralToken;
        }

        protected override async Task<AbstractSyntaxContext> CreateContext(Document document, int position, CancellationToken cancellationToken)
        {
            var workspace = document.Project.Solution.Workspace;
            var span = new TextSpan(position, 0);
            var semanticModel = await document.GetSemanticModelForSpanAsync(span, cancellationToken).ConfigureAwait(false);
            return CSharpSyntaxContext.CreateContext(workspace, semanticModel, position, cancellationToken);
        }

        protected override ValueTuple<string, string> GetDisplayAndInsertionText(ISymbol symbol, AbstractSyntaxContext context)
        {
            var insertionText = GetInsertionText(symbol, context);
            var displayText = symbol.GetArity() == 0 ? insertionText : string.Format("{0}<>", insertionText);

            return ValueTuple.Create(displayText, insertionText);
        }

        private static CompletionItemRules s_importDirectiveRules =
            CompletionItemRules.Create(commitCharacterRules: ImmutableArray.Create(CharacterSetModificationRule.Create(CharacterSetModificationKind.Replace, '.', ';')));
        private static CompletionItemRules s_importDirectiveRules_Preselect = s_importDirectiveRules.WithSelectionBehavior(CompletionItemSelectionBehavior.HardSelection);
        private static CompletionItemRules s_PreselectionRules = CompletionItemRules.Default.WithSelectionBehavior(CompletionItemSelectionBehavior.HardSelection);

        protected override CompletionItemRules GetCompletionItemRules(List<ISymbol> symbols, AbstractSyntaxContext context, bool preselect)
        {
            if (context.IsInImportsDirective)
            {
                return preselect ? s_importDirectiveRules_Preselect : s_importDirectiveRules;
            }
            else
            {
                return preselect ? s_PreselectionRules : CompletionItemRules.Default;
            }
        }

        protected override CompletionItemRules GetCompletionItemRules(IReadOnlyList<ISymbol> symbols, AbstractSyntaxContext context)
        {
            // Unused
            throw new NotImplementedException();
        }

        protected override CompletionItemSelectionBehavior PreselectedItemSelectionBehavior
        {
            get
            {
                return CompletionItemSelectionBehavior.HardSelection;
            }
        }
    }
}
