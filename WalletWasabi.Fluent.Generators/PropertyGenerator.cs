﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace WalletWasabi.Fluent.Generators
{
	[Generator]
	public class PropertyGenerator : ISourceGenerator
	{
		private const string AttributeText = @"// <auto-generated />
using System;
namespace WalletWasabi.Fluent
{
    public enum PropertyModifier
    {
        Public = 0,
        Protected = 1,
        Internal = 2,
        Private = 3
    }
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class PropertyAttribute : Attribute
    {
        public PropertyAttribute(
            string propertyName,
            Type propertyType,
            bool isReadOnly = false,
            PropertyModifier getterModifier = PropertyModifier.Public,
            PropertyModifier setterModifier = PropertyModifier.Public)
        {
            PropertyName = propertyName;
            PropertyType = propertyType;
            IsReadOnly = isReadOnly;
            GetterModifier = getterModifier;
            SetterModifier = setterModifier;
        }
        public string PropertyName { get; set; }
        public Type PropertyType { get; set; }
        public bool IsReadOnly { get; set; }
        public PropertyModifier GetterModifier { get; set; }
        public PropertyModifier SetterModifier { get; set; }
    }
}";

		public void Initialize(GeneratorInitializationContext context)
		{
			// System.Diagnostics.Debugger.Launch();
			context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
		}

		public void Execute(GeneratorExecutionContext context)
		{
			context.AddSource("PropertyAttribute", SourceText.From(AttributeText, Encoding.UTF8));

			if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
			{
				return;
			}

			var options = (context.Compilation as CSharpCompilation)?.SyntaxTrees[0].Options as CSharpParseOptions;
			var compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(AttributeText, Encoding.UTF8), options));

			var attributeSymbol = compilation.GetTypeByMetadataName("WalletWasabi.Fluent.PropertyAttribute");
			if (attributeSymbol is null)
			{
				return;
			}

			var notifySymbol = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");
			if (notifySymbol is null)
			{
				return;
			}

			List<INamedTypeSymbol> namedTypeSymbols = new();

			foreach (var candidateClass in receiver.CandidateClasses)
			{
				var semanticModel = compilation.GetSemanticModel(candidateClass.SyntaxTree);
				var namedTypeSymbol = semanticModel.GetDeclaredSymbol(candidateClass);
				if (namedTypeSymbol is null)
				{
					continue;
				}

				var attributes = namedTypeSymbol.GetAttributes();
				if (attributes.Any(ad => ad?.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) ?? false))
				{
					namedTypeSymbols.Add(namedTypeSymbol);
				}
			}

			foreach (var namedTypeSymbol in namedTypeSymbols)
			{
				var classSource = ProcessClass(namedTypeSymbol, attributeSymbol, notifySymbol);
				if (classSource is not null)
				{
					context.AddSource($"{namedTypeSymbol.Name}_Properties.cs", SourceText.From(classSource, Encoding.UTF8));
				}
			}
		}

		private static string? ProcessClass(INamedTypeSymbol classSymbol, ISymbol attributeSymbol, INamedTypeSymbol notifySymbol)
		{
			if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
			{
				return null;
			}

			string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
			bool addNotifyInterface = !classSymbol.Interfaces.Contains(notifySymbol);

			var source = new StringBuilder();

			if (addNotifyInterface)
			{
				source.Append($@"// <auto-generated />
namespace {namespaceName}
{{
    public partial class {classSymbol.Name} : {notifySymbol.ToDisplayString()}
    {{
");
				source.Append(
					"        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;");
			}
			else
			{
				source.Append($@"// <auto-generated />
namespace {namespaceName}
{{
    public partial class {classSymbol.Name}
    {{
");
			}

			var attributes = classSymbol
				.GetAttributes()
				.Where(ad => ad?.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) ?? false);

			static string ToModifier(int value)
			{
				return value switch
				{
					// Public
					0 => "",
					// Protected
					1 => "protected ",
					// Internal
					2 => "internal ",
					// Private
					3 => "private ",
					// Default
					_ => ""
				};
			}

			foreach (var attributeData in attributes)
			{
				if (attributeData is null || attributeData.ConstructorArguments.Length != 5)
				{
					continue;
				}

				var propertyName = (string?) attributeData.ConstructorArguments[0].Value;
				var propertyType = attributeData.ConstructorArguments[1].Value;
				var isReadOnly = (bool?) attributeData.ConstructorArguments[2].Value;
				var getterModifier = (int?) attributeData.ConstructorArguments[3].Value;
				var setterModifier = (int?) attributeData.ConstructorArguments[4].Value;

				if (propertyName is null || propertyType is null || isReadOnly is null || getterModifier is null || setterModifier is null)
				{
					continue;
				}

				var fieldName = $"_{propertyName.Substring(0, 1).ToLower() + propertyName.Substring(1)}";

				source.Append($@"
private  {propertyType} {fieldName};");

				source.Append($@"
public {propertyType} {propertyName}
{{
    {ToModifier(getterModifier.Value)}get
    {{
        return this.{fieldName};
    }}");

				if (!isReadOnly.Value)
				{
					source.Append($@"
    {ToModifier(setterModifier.Value)}set
    {{
        if (!Equals({fieldName}, value))
        {{
            {fieldName} = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof({propertyName})));
        }}
    }}");
				}

				source.Append($@"
}}
");
			}

			source.Append($@"
    }}
}}");

			return source.ToString();
		}

		private class SyntaxReceiver : ISyntaxReceiver
		{
			public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

			public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
			{
				if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax
				    && classDeclarationSyntax.AttributeLists.Count > 0)
				{
					CandidateClasses.Add(classDeclarationSyntax);
				}
			}
		}
	}
}