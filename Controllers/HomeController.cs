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


    public class ReviewResult
    {
        public int Line { get; set; }
        public string Message { get; set; }

        public int Severity { get; set; }

        public string HelpText { get; set; }
    }

    [HttpPost]
    public List<ReviewResult> ReviewCode([FromBody] InputParam model)
    {
        List<ReviewResult> result = new List<ReviewResult>();

        
        string? code = model?.Code;

        // if (string.IsNullOrEmpty(code))
        // {
        //     return Content("Error: Please provide some code to review.", "text/plain");
        // }

        try
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            //StringBuilder review = new StringBuilder();

            // Rule 1: Syntax Errors
            var diagnostics = tree.GetDiagnostics();
            foreach (var diagnostic in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
                //review.AppendLine($"Line {line}: Syntax error - {diagnostic.GetMessage()}");
                result.Add(new ReviewResult { Line = line, Message = $"Syntax error - {diagnostic.GetMessage()}" });
            }

            // if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            //     return Content(review.ToString(), "text/plain");

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
                //review.AppendLine($"Line {line}: Public field '{field.Declaration.Variables.First().Identifier}' should be a property.");
                result.Add(new ReviewResult { Line = line, Message = $"Public field '{field.Declaration.Variables.First().Identifier}' should be a property." });
            }

            // Rule 3: Long Methods
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                int lineCount = method.GetText().Lines.Count;
                if (lineCount > 50)
                {
                    var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    //review.AppendLine($"Line {line}: Method '{method.Identifier}' is too long ({lineCount} lines). Consider splitting.");
                    result.Add(new ReviewResult { Line = line, Message = $"Method '{method.Identifier}' is too long ({lineCount} lines). Consider splitting." });
                }
            }

            // Rule 4: Unused Variables
            var variables = root.DescendantNodes().OfType<VariableDeclaratorSyntax>();
            foreach (var variable in variables)
            {
                var symbol = semanticModel.GetDeclaredSymbol(variable);
                var references = symbol.DeclaringSyntaxReferences;
                if (references.Length == 1)
                {
                    var line = variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    //review.AppendLine($"Line {line}: Variable '{variable.Identifier}' is unused.");
                    result.Add(new ReviewResult { Line = line, Message = $"Variable '{variable.Identifier}' is unused." });
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
                            //review.AppendLine($"Line {line}: Unreachable code after return.");
                            result.Add(new ReviewResult { Line = line, Message = $"Unreachable code after return." });

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
                                    //review.AppendLine($"Line {line}: Potential null dereference on '{id.Identifier}'.");
                                    result.Add(new ReviewResult { Line = line, Message = $"Potential null dereference on '{id.Identifier}'." });

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
                    //review.AppendLine($"Line {line}: Async method '{method.Identifier}' lacks 'await'. Consider removing 'async' or adding 'await'.");
                    result.Add(new ReviewResult { Line = line, Message = $"Async method '{method.Identifier}' lacks 'await'. Consider removing 'async' or adding 'await'." });

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
                    //review.AppendLine($"Line {line}: Disposable resource '{symbol.Name}' should be wrapped in a 'using' statement.");
                    result.Add(new ReviewResult { Line = line, Message = $"Disposable resource '{symbol.Name}' should be wrapped in a 'using' statement." });

                }
            }

            // Rule 8: Controller Action Issues
            var controllers = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => semanticModel.GetDeclaredSymbol(c).BaseType?.Name.Contains("Controller") == true);
            foreach (var controller in controllers)
            {
                var actions = controller.DescendantNodes().OfType<MethodDeclarationSyntax>();
                foreach (var action in actions)
                {
                    if (!action.Modifiers.Any(SyntaxKind.PublicKeyword))
                    {
                        var line = action.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        //review.AppendLine($"Line {line}: Controller action '{action.Identifier}' should be public.");
                        result.Add(new ReviewResult { Line = line, Message = $"Controller action '{action.Identifier}' should be public." });
                    }
                }
            }

            // Rule 9: Static HttpClient Usage
            var httpClients = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer != null &&
                            semanticModel.GetTypeInfo(v.Initializer.Value).Type?.Name == "HttpClient" &&
                            !v.Ancestors().OfType<FieldDeclarationSyntax>().Any(f => f.Modifiers.Any(SyntaxKind.StaticKeyword)));
            foreach (var httpClient in httpClients)
            {
                var line = httpClient.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                //review.AppendLine($"Line {line}: HttpClient '{httpClient.Identifier}' should be static or managed as a singleton to avoid socket exhaustion.");
                result.Add(new ReviewResult { Line = line, Message = $"HttpClient '{httpClient.Identifier}' should be static or managed as a singleton to avoid socket exhaustion." });
            }

            // Rule 10: Missing [ApiController] Attribute
            foreach (var controller in controllers)
            {
                var symbol = semanticModel.GetDeclaredSymbol(controller);
                var attributes = symbol.GetAttributes();
                if (!attributes.Any(a => a.AttributeClass?.Name == "ApiControllerAttribute"))
                {
                    var line = controller.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    //review.AppendLine($"Line {line}: Controller '{controller.Identifier}' is missing [ApiController] attribute, recommended for .NET Core APIs.");
                    result.Add(new ReviewResult { Line = line, Message = $"Controller '{controller.Identifier}' is missing [ApiController] attribute, recommended for .NET Core APIs." });
                }
            }

            // Rule 11: Action Methods Without HTTP Attributes
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
                        //review.AppendLine($"Line {line}: Action '{action.Identifier}' lacks an HTTP attribute (e.g., [HttpGet]).");
                        result.Add(new ReviewResult { Line = line, Message = $"Action '{action.Identifier}' lacks an HTTP attribute (e.g., [HttpGet])." });

                    }
                }
            }

            // Rule 12: Task Return Without Async
            foreach (var method in methods)
            {
                var returnType = semanticModel.GetTypeInfo(method.ReturnType).Type;
                if (returnType != null && (returnType.Name == "Task" || returnType.Name.StartsWith("Task<")) &&
                    !method.Modifiers.Any(SyntaxKind.AsyncKeyword))
                {
                    var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    //review.AppendLine($"Line {line}: Method '{method.Identifier}' returns Task but is not async. Use async/await or return a completed Task.");
                    result.Add(new ReviewResult { Line = line, Message = $"returns Task but is not async. Use async/await or return a completed Task." });

                }
            }

            // if (review.Length == 0)
            // {
            //     review.AppendLine("No major issues detected. Code looks good!");
            // }

            //return Content(review.ToString(), "text/plain");

            return result;
        }
        catch (Exception)
        {
            //return Content($"Error reviewing code: {ex.Message}", "text/plain");
            throw;
        }
    }



}
