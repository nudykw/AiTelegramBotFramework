# Step-by-Step Security Scan Setup Plan (Local & CI/CD)

This plan outlines the staged approach to establishing static application security testing (SAST) and software composition analysis (SCA) for the project, starting with local checks and progressing to GitHub integration.

---

## Phase 1: Local Security Scanning (Pre-flight Checks)

Before committing changes to the remote repository, ensure all dependencies and source code are validated locally.

### Step 1.1: Built-in NuGet Dependency Scan
.NET SDK provides built-in vulnerability auditing for referenced packages.

1. Run the following command in the solution directory:
   ```bash
   dotnet list package --vulnerable --include-transitive
   ```
2. If any vulnerable packages are detected, upgrade them to safe versions in their respective `.csproj` files.

### Step 1.2: Roslyn Security Analyzers
Roslyn Analyzers inspect C# code during compilation. We will configure the project to enforce security standards.

1. Ensure the SDK analyzers are active by verifying or adding the following properties to a shared directory configuration or individual `.csproj` files:
   ```xml
   <PropertyGroup>
     <EnableNETAnalyzers>true</EnableNETAnalyzers>
     <AnalysisLevel>latest</AnalysisLevel>
     <AnalysisMode>AllEnabledByDefault</AnalysisMode>
   </PropertyGroup>
   ```
2. Customize the analysis rules using `.editorconfig` in the root of the project to treat security issues as warnings or errors:
   ```ini
   # Enable all security rules as warnings or errors
   dotnet_analyzer_diagnostic.category-Security.severity = warning
   ```

### Step 1.3: Microsoft DevSkim Setup
DevSkim provides inline security analysis for developer environments and CLI checks.

1. Install the DevSkim command-line tool globally:
   ```bash
   dotnet tool install --global Microsoft.DevSkim.CLI
   ```
2. Run a scan on the codebase to search for crypto vulnerabilities, hardcoded secrets, and unsafe API calls:
   ```bash
   devskim analyze -s .
   ```
3. (Optional) Review findings and configure ignore rules or exclusions for test projects if needed.

### Step 1.4: Snyk CLI Local Scanning
The Snyk CLI verifies NuGet packages and static code logic.

1. Install Snyk CLI via npm (requires Node.js):
   ```bash
   npm install -g snyk
   ```
2. Authenticate the CLI against your Snyk account:
   ```bash
   snyk auth
   ```
3. Test NuGet packages for vulnerabilities:
   ```bash
   snyk test
   ```
4. Test source code logic (SAST):
   ```bash
   snyk code test
   ```

---

## Phase 2: GitHub Security Integration & Status Badges

Once local scans run successfully, automate these checks using GitHub tools.

### Step 2.1: GitHub CodeQL Analysis
GitHub's native SAST engine will scan code on push or pull request.

1. Create a GitHub Actions workflow file: `.github/workflows/codeql.yml`
2. Populate the file with standard CodeQL actions targeting C#:
   ```yaml
   name: "CodeQL"

   on:
     push:
       branches: [ "main", "master" ]
     pull_request:
       branches: [ "main", "master" ]
     schedule:
       - cron: '0 0 * * 1' # Run weekly on Mondays

   jobs:
     analyze:
       name: Analyze C#
       runs-on: ubuntu-latest
       permissions:
         actions: read
         contents: read
         security-events: write

       steps:
       - name: Checkout repository
         uses: actions/checkout@v4

       - name: Initialize CodeQL
         uses: github/codeql-action/init@v3
         with:
           languages: 'csharp'

       - name: Autobuild
         uses: github/codeql-action/autobuild@v3

       - name: Perform CodeQL Analysis
         uses: github/codeql-action/analyze@v3
   ```
3. Add the GitHub Action build status badge to the top of `README.md`:
   ```markdown
   [![CodeQL Status](https://github.com/<OWNER>/<REPO>/actions/workflows/codeql.yml/badge.svg)](https://github.com/<OWNER>/<REPO>/actions/workflows/codeql.yml)
   ```

### Step 2.2: Snyk App Integration
To obtain the Snyk vulnerability status badge, configure the Snyk GitHub App.

1. Log in to [snyk.io](https://snyk.io/) and navigate to **Integrations -> GitHub**.
2. Grant access to the target repository.
3. Import the repository into your Snyk dashboard. Snyk will scan the repository and monitor `.csproj` files on every commit.
4. Obtain the markdown badge from the Snyk project dashboard under **Settings -> Badges** and append it to `README.md`:
   ```markdown
   [![Known Vulnerabilities](https://snyk.io/test/github/<OWNER>/<REPO>/badge.svg)](https://snyk.io/test/github/<OWNER>/<REPO>)
   ```

---

## Verification Plan

### Local Verification
- [ ] Running `dotnet list package --vulnerable --include-transitive` returns 0 vulnerabilities or lists known packages to be updated.
- [ ] Running `devskim analyze -s .` succeeds without critical warnings.
- [ ] Running `snyk test` and `snyk code test` passes with zero severe alerts.

### CI/CD Verification
- [ ] Push a feature branch to trigger the CodeQL Action. Ensure the job completes successfully.
- [ ] Verify that CodeQL findings are displayed under the repository's **Security -> Code scanning** tab.
- [ ] Ensure Snyk dashboard successfully parses the GitHub commits and updates the README badge correctly.
