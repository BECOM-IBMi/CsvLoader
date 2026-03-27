# Skill: .NET Cross-Platform CI & Single-Binary Publish

**Owner**: Wedge (DevOps)  
**Tags**: .NET, GitHub Actions, CI/CD, single-binary, cross-platform  
**Status**: Proven (CsvLoader v1)

---

## Problem

Build and publish a .NET console application as a single self-contained binary for multiple platforms (Windows & Linux) using GitHub Actions.

---

## Solution

### 1. Project Configuration (.csproj)

Add properties to enable single-file publish by default:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <!-- Enable single-file publish -->
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
</PropertyGroup>
```

**Why**: Ensures consistent behavior locally and in CI. Developers don't need to remember CLI flags.

---

### 2. GitHub Actions Workflow (Matrix Strategy)

Use a **build matrix** to run on multiple OS runners. Each runner publishes for its native platform:

```yaml
name: CI

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  build-and-test:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
    runs-on: ${{ matrix.os }}
    
    steps:
    - uses: actions/checkout@v4
      with:
        fetch-depth: 0  # For GitVersion
    
    - name: Install GitVersion
      uses: gittools/actions/gitversion/setup@v3
      with:
        versionSpec: '6.x'
    
    - name: Determine version
      id: gitversion
      uses: gittools/actions/gitversion/execute@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.x'  # Adjust version as needed
    
    - name: Restore
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore --configuration Release
    
    - name: Test
      run: dotnet test --no-build --configuration Release --verbosity normal
    
    - name: Publish (Windows)
      if: matrix.os == 'windows-latest'
      run: |
        dotnet publish src/MyApp/MyApp.csproj `
          --configuration Release `
          --runtime win-x64 `
          --self-contained true `
          -p:PublishSingleFile=true `
          -p:VersionPrefix=${{ steps.gitversion.outputs.MajorMinorPatch }} `
          -o publish/win-x64
    
    - name: Publish (Linux)
      if: matrix.os == 'ubuntu-latest'
      run: |
        dotnet publish src/MyApp/MyApp.csproj \
          --configuration Release \
          --runtime linux-x64 \
          --self-contained true \
          -p:PublishSingleFile=true \
          -p:VersionPrefix=${{ steps.gitversion.outputs.MajorMinorPatch }} \
          -o publish/linux-x64
    
    - name: Upload artifact (Windows)
      if: matrix.os == 'windows-latest'
      uses: actions/upload-artifact@v4
      with:
        name: myapp-win-x64
        path: publish/win-x64/
    
    - name: Upload artifact (Linux)
      if: matrix.os == 'ubuntu-latest'
      uses: actions/upload-artifact@v4
      with:
        name: myapp-linux-x64
        path: publish/linux-x64/
```

**Key Points**:
- Use platform-specific `if:` conditions to run publish steps only on the appropriate runner.
- Runtime IDs: `win-x64`, `linux-x64`, `osx-x64`, etc.
- GitVersion output feeds into `-p:VersionPrefix` for automatic semantic versioning.
- Artifacts are uploaded for downstream jobs (e.g., release, integration tests).

---

### 3. Release Workflow (Optional)

For tagged releases, create a separate workflow that downloads artifacts and publishes a GitHub Release:

```yaml
name: Release

on:
  push:
    tags:
      - 'v*.*.*'

jobs:
  build-release:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
    runs-on: ${{ matrix.os }}
    
    steps:
    # ... same build steps as CI workflow ...
  
  create-release:
    needs: build-release
    runs-on: ubuntu-latest
    permissions:
      contents: write
    
    steps:
    - uses: actions/download-artifact@v4
    
    - name: Create GitHub Release
      uses: softprops/action-gh-release@v1
      with:
        files: |
          myapp-win-x64/*
          myapp-linux-x64/*
        draft: false
        prerelease: false
```

**Benefit**: Automates release creation; binaries are attached as assets.

---

## Checklist for New Project

- [ ] Add `<PublishSingleFile>true</PublishSingleFile>` and `<SelfContained>true</SelfContained>` to .csproj
- [ ] Create `.github/workflows/ci.yml` with matrix strategy for [ubuntu-latest, windows-latest]
- [ ] Integrate GitVersion (install + execute steps)
- [ ] Use platform-specific `if:` conditions for publish steps
- [ ] Update runtime IDs if targeting architectures beyond x64
- [ ] Upload artifacts for later use (releases, distribution)
- [ ] (Optional) Create `.github/workflows/release.yml` for tagged releases

---

## Outputs

- **Local build**: `dotnet publish -c Release` → self-contained binary in `bin/Release/net<version>/<rid>/publish/`
- **CI build**: Artifacts uploaded to GitHub (named `myapp-win-x64`, `myapp-linux-x64`, etc.)
- **Release**: GitHub Release with binaries as downloadable assets (if using release.yml)

---

## Notes

- Self-contained binaries are larger (~70MB for minimal .NET console app) because they include the runtime.
- RuntimeIdentifier is specific to platform. Let the OS matrix handle it rather than hardcoding.
- GitVersion must have `fetch-depth: 0` in checkout to access full commit history.
- For ARM64 support, add `arm64` to matrix or use conditional platform detection.
