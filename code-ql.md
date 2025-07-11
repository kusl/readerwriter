## What is CodeQL?

CodeQL is GitHub's semantic code analysis engine that treats code as data. Instead of using pattern matching, it creates a database from your code and lets you query it like you would a SQL database.

### How it works:
1. **Code → Database**: Extracts a relational database from your source code
2. **Query Language**: Uses a SQL-like language to find security vulnerabilities and bugs
3. **Semantic Analysis**: Understands code flow, not just syntax patterns

### Key Features:
- Finds complex vulnerabilities (SQL injection, XSS, buffer overflows, etc.)
- Tracks data flow through your application
- Language-specific security rules
- Customizable queries

## Running CodeQL Locally

Yes! You can absolutely run CodeQL locally. Here's how:

### 1. **Install CodeQL CLI**

```bash
# Download from GitHub releases
# https://github.com/github/codeql-cli-binaries/releases

# Or use homebrew on macOS
brew install codeql

# Verify installation
codeql --version
```

### 2. **Get Query Packs**

```bash
# Clone the CodeQL queries repository
git clone https://github.com/github/codeql.git
cd codeql
```

### 3. **Create a Database**

```bash
# For your C# project
codeql database create my-database --language=csharp --source-root=/path/to/your/project
```

### 4. **Run Analysis**

```bash
# Run standard queries
codeql database analyze my-database csharp-security-and-quality.qls --format=sarif-latest --output=results.sarif

# Or run specific queries
codeql database analyze my-database /path/to/codeql/csharp/ql/src/Security --format=csv --output=results.csv
```

### 5. **VS Code Extension** (Easier Option)

Install the [CodeQL extension for VS Code](https://marketplace.visualstudio.com/items?itemName=GitHub.vscode-codeql):
- Provides a GUI interface
- Syntax highlighting for QL queries
- Run queries with a click
- View results inline

### Example: Custom Query

Create a file `FindTodos.ql`:

```ql
import csharp

from Comment c
where c.getText().regexpMatch(".*TODO.*")
select c, "TODO comment found"
```

Run it:
```bash
codeql query run FindTodos.ql --database=my-database
```

### Local vs GitHub Actions

**Local advantages:**
- Faster feedback during development
- Debug specific issues
- Write and test custom queries
- No internet required

**GitHub Actions advantages:**
- Automated on every commit
- Centralized results
- Team visibility
- Integration with PR checks

### Quick Start Script

Here's a script to get started locally:

```bash
#!/bin/bash
# setup-codeql.sh

# Create database
codeql database create codeql-db --language=csharp

# Analyze with security queries
codeql database analyze codeql-db \
  microsoft/codeql:csharp-queries \
  --format=sarif-latest \
  --output=security-results.sarif

# View results
echo "Results saved to security-results.sarif"
echo "Upload to GitHub or view with SARIF viewer"
```

### Tips:
- Start with pre-built query suites
- Use VS Code extension for easier learning
- Check results before committing
- Integrate into pre-commit hooks
- Custom queries for project-specific patterns

CodeQL is powerful for finding deep security issues that simple linters miss, and running it locally gives you immediate feedback during development!