using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyASPNetCoreApp.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace MyASPNetCoreApp.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public class InputParam
    {
        public string Code { get; set; }
    }

    public enum ErrorTypeEnum
    {
        Message = 0,
        Warning = 1,
        Error = 2
    }

    public class ReviewResult
    {
        public int Line { get; set; }
        public string Message { get; set; }

        public int ErrorType { get; set; }

        public string HelpURL { get; set; }
    }



    private List<ReviewResult> GetCodeReviewResult(string code)
    {
        List<ReviewResult> result = new List<ReviewResult>();
        try
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();

            // Rule 1: Syntax Errors
            var diagnostics = tree.GetDiagnostics();
            foreach (var diagnostic in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
                result.Add(new ReviewResult
                {
                    Line = line,
                    Message = $"Syntax error - {diagnostic.GetMessage()}",
                    ErrorType = (int)ErrorTypeEnum.Error,
                    HelpURL = "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/"
                });
            }

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) && result.Count() > 0)
            {
                return result;
            }
            var compilation = CSharpCompilation.Create("ReviewCompilation")
                .AddSyntaxTrees(tree)
                .AddReferences(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Net.Http.HttpClient).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(ControllerBase).Assembly.Location)
                );

            var semanticModel = compilation.GetSemanticModel(tree);

            // Rule 2: Public Fields
            var publicFields = root.DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .Where(f => f.Modifiers.Any(SyntaxKind.PublicKeyword));
            foreach (var field in publicFields)
            {
                var line = field.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                result.Add(new ReviewResult
                {
                    Line = line,
                    Message = $"Public field '{field.Declaration.Variables.First().Identifier}' should be a property.",
                    ErrorType = (int)ErrorTypeEnum.Warning,
                    HelpURL = "https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/field"
                });
            }

            // Rule 3: Long Methods
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                int lineCount = method.GetText().Lines.Count;
                if (lineCount > 50)
                {
                    var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    result.Add(new ReviewResult
                    {
                        Line = line,
                        Message = $"Method '{method.Identifier}' is too long ({lineCount} lines). Consider splitting.",
                        ErrorType = (int)ErrorTypeEnum.Warning,
                        HelpURL = "https://learn.microsoft.com/en-us/visualstudio/code-quality/ca1502"
                    });
                }
            }

            // Fixed Rule 4: Unused Variables with block-scoped analysis
            var variables = root.DescendantNodes().OfType<VariableDeclaratorSyntax>();
            foreach (var variable in variables)
            {
                var symbol = semanticModel.GetDeclaredSymbol(variable);
                if (symbol == null) continue;

                var references = root.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Select(id => new { id, resolved = semanticModel.GetSymbolInfo(id).Symbol })
                    .Where(pair => pair.resolved != null &&
                                   SymbolEqualityComparer.Default.Equals(pair.resolved, symbol))
                    .Select(pair => pair.id)
                    .Where(id => id.GetLocation().SourceSpan.Start != variable.Identifier.SpanStart); // Exclude self

                if (!references.Any())
                {
                    var line = variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    result.Add(new ReviewResult
                    {
                        Line = line,
                        Message = $"Variable '{variable.Identifier}' is unused.",
                        ErrorType = (int)ErrorTypeEnum.Warning,
                        HelpURL = "https://learn.microsoft.com/en-us/visualstudio/ide/reference/unused-values-parameters?view=vs-2022"
                    });
                }
            }


            // Rule 5: Unreachable Code and Null Dereferences
            foreach (var method in methods)
            {
                var statements = method.Body?.Statements;
                if (statements != null)
                {
                    bool afterReturn = false;
                    foreach (var statement in statements)
                    {
                        if (afterReturn)
                        {
                            var line = statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            result.Add(new ReviewResult
                            {
                                Line = line,
                                Message = $"Unreachable code after return.",
                                ErrorType = (int)ErrorTypeEnum.Warning,
                                HelpURL = "https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0162"
                            });

                        }
                        if (statement is ReturnStatementSyntax)
                            afterReturn = true;

                        var identifiers = statement.DescendantNodes().OfType<IdentifierNameSyntax>();
                        foreach (var id in identifiers)
                        {
                            var symbolInfo = semanticModel.GetSymbolInfo(id);
                            if (symbolInfo.Symbol is ILocalSymbol local && local.NullableAnnotation == NullableAnnotation.None)
                            {
                                var decl = variables.FirstOrDefault(v => v.Identifier.Text == id.Identifier.Text);
                                if (decl != null && decl.Initializer == null)
                                {
                                    var line = id.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                                    result.Add(new ReviewResult
                                    {
                                        Line = line,
                                        Message = $"Potential null dereference on '{id.Identifier}'.",
                                        ErrorType = (int)ErrorTypeEnum.Warning,
                                        HelpURL = "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0162"
                                    });

                                }
                            }
                        }
                    }
                }
            }

            // Rule 6: Async/Await Misuse
            foreach (var method in methods.Where(m => m.Modifiers.Any(SyntaxKind.AsyncKeyword)))
            {
                if (!method.DescendantNodes().OfType<AwaitExpressionSyntax>().Any())
                {
                    var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    result.Add(new ReviewResult
                    {
                        Line = line,
                        Message = $"Async method '{method.Identifier}' lacks 'await'. Consider removing 'async' or adding 'await'.",
                        ErrorType = (int)ErrorTypeEnum.Error,
                        HelpURL = "https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/async/"
                    });

                }
            }

            // Rule 7: Disposable Resources
            var disposables = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer != null &&
                            semanticModel.GetTypeInfo(v.Initializer.Value).Type?.AllInterfaces.Any(i => i.Name == "IDisposable") == true);
            foreach (var disposable in disposables)
            {
                var symbol = semanticModel.GetDeclaredSymbol(disposable);
                var containingBlock = disposable.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
                if (containingBlock != null && !containingBlock.DescendantNodes().OfType<UsingStatementSyntax>().Any(u => u.DescendantNodes().Contains(disposable)))
                {
                    var line = disposable.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    result.Add(new ReviewResult
                    {
                        Line = line,
                        Message = $"Disposable resource '{symbol.Name}' should be wrapped in a 'using' statement.",
                        ErrorType = (int)ErrorTypeEnum.Warning,
                        HelpURL = "https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose"
                    });

                }
            }

            // Rule 8: Static HttpClient Usage
            var httpClients = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer != null &&
                            semanticModel.GetTypeInfo(v.Initializer.Value).Type?.Name == "HttpClient" &&
                            !v.Ancestors().OfType<FieldDeclarationSyntax>().Any(f => f.Modifiers.Any(SyntaxKind.StaticKeyword)));
            foreach (var httpClient in httpClients)
            {
                var line = httpClient.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                result.Add(new ReviewResult
                {
                    Line = line,
                    Message = $"HttpClient '{httpClient.Identifier}' should be static or managed as a singleton to avoid socket exhaustion.",
                    ErrorType = (int)ErrorTypeEnum.Warning,
                    HelpURL = "https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/httpclient-guidelines"
                });
            }

            var controllers = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => semanticModel.GetDeclaredSymbol(c).BaseType?.Name.Contains("Controller") == true);

            // Rule 9: Missing [ApiController] Attribute
            foreach (var controller in controllers)
            {
                var symbol = semanticModel.GetDeclaredSymbol(controller);
                var attributes = symbol.GetAttributes();
                if (!attributes.Any(a => a.AttributeClass?.Name == "ApiControllerAttribute"))
                {
                    var line = controller.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    result.Add(new ReviewResult
                    {
                        Line = line,
                        Message = $"Controller '{controller.Identifier}' is missing [ApiController] attribute, recommended for .NET Core APIs.",
                        ErrorType = (int)ErrorTypeEnum.Warning,
                        HelpURL = "https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0#apicontroller-attribute"
                    });
                }
            }

            // Rule 10: Action Methods Without HTTP Attributes
            foreach (var controller in controllers)
            {
                var actions = controller.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Modifiers.Any(SyntaxKind.PublicKeyword));
                foreach (var action in actions)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(action);
                    var attributes = symbol.GetAttributes();
                    if (!attributes.Any(a => a.AttributeClass?.Name.Contains("Http") == true))
                    {
                        var line = action.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        result.Add(new ReviewResult
                        {
                            Line = line,
                            Message = $"Action '{action.Identifier}' lacks an HTTP attribute (e.g., [HttpGet]).",
                            ErrorType = (int)ErrorTypeEnum.Warning,
                            HelpURL = "https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0#http-verb-attributes"
                        });

                    }
                }
            }

            // Rule 11: Task Return Without Async
            foreach (var method in methods)
            {
                var returnType = semanticModel.GetTypeInfo(method.ReturnType).Type;
                if (returnType != null && (returnType.Name == "Task" || returnType.Name.StartsWith("Task<")) &&
                    !method.Modifiers.Any(SyntaxKind.AsyncKeyword))
                {
                    var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    result.Add(new ReviewResult
                    {
                        Line = line,
                        Message = $"returns Task but is not async. Use async/await or return a completed Task.",
                        ErrorType = (int)ErrorTypeEnum.Warning,
                        HelpURL = "https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/async/#async-methods"
                    });
                }
            }

            // Rule 12: Empty Catch Blocks
            var emptyCatches = root.DescendantNodes().OfType<CatchClauseSyntax>()
                .Where(c => !c.Block.Statements.Any());
            foreach (var catchBlock in emptyCatches)
            {
                var line = catchBlock.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                result.Add(new ReviewResult
                {
                    Line = line,
                    Message = "Empty catch block detected. Consider logging or handling the exception.",
                    ErrorType = (int)ErrorTypeEnum.Warning,
                    HelpURL = "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/try-catch"
                });
            }

            // Rule 13: Deep Nesting (more than 6 levels)
            var allBlocks = root.DescendantNodes().OfType<BlockSyntax>();
            foreach (var block in allBlocks)
            {
                int depth = 0;
                SyntaxNode current = block;
                while ((current = current.Parent) != null)
                {
                    if (current is BlockSyntax) depth++;
                    if (depth > 6)
                    {
                        var line = block.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        result.Add(new ReviewResult
                        {
                            Line = line,
                            Message = "Code is nested too deeply (more than 6 levels). Consider refactoring.",
                            ErrorType = (int)ErrorTypeEnum.Warning,
                            HelpURL = "https://refactoring.guru/smells/deep-nesting"
                        });
                        break;
                    }
                }
            }

            // Rule 14: Missing XML Documentation on Public Methods
            var publicMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(SyntaxKind.PublicKeyword));
            foreach (var method in publicMethods)
            {
                var trivia = method.GetLeadingTrivia();
                bool hasDoc = trivia.Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                              t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
                if (!hasDoc)
                {
                    var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    result.Add(new ReviewResult
                    {
                        Line = line,
                        Message = $"Public method '{method.Identifier}' lacks XML documentation.",
                        ErrorType = (int)ErrorTypeEnum.Warning,
                        HelpURL = "https://learn.microsoft.com/en-us/dotnet/csharp/codedoc"
                    });
                }
            }

            return result;
        }
        catch (Exception)
        {
            throw;
        }
    }

    [HttpPost]
    public List<ReviewResult> ReviewCode([FromBody] InputParam model)
    {
        string? code = model?.Code;

        return GetCodeReviewResult(code);
    }
}
