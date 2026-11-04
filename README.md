# .NET Core Static Analysis Code Review Tool

A web-based C# code review tool built with ASP.NET Core MVC and Roslyn. It analyses submitted C# source code against a collection of predefined coding and best-practice rules, then displays findings with line references to help developers identify potential issues.

## Overview

This project explores how Roslyn, the .NET compiler platform, can be used to inspect C# source code programmatically and perform static code analysis.

Users can enter C# code into a browser-based editor and submit it for analysis. The application parses the source code, examines its syntax and semantic information, and returns a list of potential issues with corresponding line numbers.

## Key Features

- **Browser-based code editor:** Write or paste C# code into an editor powered by CodeMirror, with syntax highlighting and line numbers.
- **Roslyn-based analysis:** Uses the Microsoft.CodeAnalysis API to parse C# source code and inspect syntax trees and semantic information.
- **Rule-based code review:** Applies predefined checks to identify potential coding issues and .NET development best-practice violations.
- **Line-level findings:** Displays detected issues with their corresponding source-code line numbers.
- **Editor navigation:** Select a finding to jump directly to the relevant line in the code editor.

## Analysis Rules

The application currently implements the following checks:

| Category | Analysis |
|---|---|
| Syntax | Detects C# syntax errors. |
| Code Quality | Identifies public fields that should generally be properties. |
| Code Quality | Flags methods exceeding 50 lines. |
| Code Quality | Checks for potentially unused variables. |
| Code Quality | Checks for unreachable statements following a return statement. |
| Null Safety | Identifies certain potential null dereference scenarios. |
| Async/Await | Flags async methods that do not contain an await expression. |
| Resource Management | Identifies disposable resources that may not be properly disposed of. |
| ASP.NET Core | Checks controller action accessibility. |
| HTTP Client | Flags non-static HttpClient instances and recommends singleton or managed usage. |
| ASP.NET Core | Checks for the [ApiController] attribute on controllers. |
| ASP.NET Core | Checks public controller actions for HTTP method attributes. |
| Async/Await | Identifies methods returning Task without the async modifier. |

*Note: These are custom heuristic checks rather than a comprehensive implementation of the C# compiler's diagnostics or a complete static-analysis framework. Results may require manual verification.*

## Technology Stack

- **Backend:** C#, ASP.NET Core MVC (.NET 9)
- **Code Analysis:** Microsoft.CodeAnalysis.CSharp (Roslyn)
- **Frontend:** Razor Views, HTML5, CSS3, JavaScript, jQuery, Bootstrap
- **Code Editor:** CodeMirror 5
- **Development Tools:** Visual Studio / Visual Studio Code

## Architecture and Workflow

The application follows an ASP.NET Core MVC architecture.

1. **Code Input:** Users enter C# source code through the CodeMirror editor.
2. **Submission:** JavaScript sends the source code to the ASP.NET Core controller through an AJAX POST request.
3. **Syntax Analysis:** Roslyn parses the submitted source code into a syntax tree and reports syntax errors.
4. **Semantic Analysis:** The application creates a Roslyn compilation and semantic model to inspect code symbols and types.
5. **Rule Evaluation:** The controller executes the predefined analysis rules against the syntax tree and semantic model.
6. **Results Display:** Findings are returned to the browser as JSON and displayed with line references.
7. **Code Navigation:** Users can select a finding to navigate to the corresponding line in the editor.

## Getting Started

### Prerequisites

- .NET 9 SDK
- Visual Studio 2022 or Visual Studio Code
- A modern web browser
- Internet connection for loading CodeMirror assets from CDN

### Installation

Clone the repository:

```bash
git clone https://github.com/tslin89/core-staticanalysis-codereview.git
```

Navigate to the project directory:

```bash
cd core-staticanalysis-codereview
```

Restore dependencies:

```bash
dotnet restore
```

Run the application:

```bash
dotnet run
```

Open the local URL displayed in the terminal. The configured launch profiles use `https://localhost:7060` or `https://localhost:8888`.

## Usage

1. Open the application in your browser.
2. Enter or paste C# source code into the editor.
3. Click **Review Code**.
4. Review the findings displayed in the results panel.
5. Select an individual finding to navigate to its corresponding line in the editor.

## Project Structure

```text
core-staticanalysis-codereview/
├── Controllers/
│   └── HomeController.cs       # Code analysis and review rules
├── Models/
│   └── ErrorViewModel.cs
├── Views/
│   ├── Home/
│   │   └── Index.cshtml        # Code editor and results interface
│   └── Shared/
├── wwwroot/
│   ├── css/
│   ├── js/
│   └── lib/
├── Program.cs                  # Application configuration
└── MyASPNetCoreApp.csproj      # Project dependencies and configuration
```

## Learning Objectives

This project provided practical experience with:

- Using Roslyn APIs for C# syntax-tree and semantic-model analysis.
- Building custom static-analysis rules for identifying potential code-quality issues.
- Understanding the relationship between syntax analysis and semantic analysis.
- Developing an ASP.NET Core MVC application with asynchronous AJAX communication.
- Integrating a browser-based code editor with a backend code-analysis service.

## Licence

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
