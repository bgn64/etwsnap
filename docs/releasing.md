# Releasing ETWSnap

Release automation runs entirely in GitHub Actions and does not require signing certificates or repository secrets.

## One-time repository setup

1. Make the repository public before distributing through Scoop. Private GitHub release assets require authentication and cannot serve as a public Scoop source.
2. Merge the release workflows into the default branch.
3. In **Settings > Actions > General > Workflow permissions**, allow read and write access for `GITHUB_TOKEN`.
4. Ensure branch protection permits `github-actions[bot]` to update `bucket/etwsnap.json`, or be prepared to apply that generated manifest manually after each stable release.

GitHub provenance attestations are generated only when the repository is public. No signing account, certificate, or repository secret is required.

The workflows use the standard `windows-2025` runner and MSVC `v143`.

## Before tagging

Run the complete interactive validation locally:

```powershell
.\eng\Build.ps1 -Version 0.1.0 -IncludeInteractiveTests
.\eng\Package.ps1 -Version 0.1.0 -SkipBuild
```

WPR trace creation should also be tested from an elevated terminal. CI runs unit tests and headless integration tests, but hosted runners do not provide a reliable interactive desktop for capture validation.

## Publish

Create and push a SemVer tag from the intended release commit:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The release workflow:

1. Builds and tests the x64 framework-dependent package.
2. Creates the portable ZIP, symbols ZIP, and SHA-256 file.
3. Smoke-tests the extracted package.
4. Generates a GitHub provenance attestation for public repositories.
5. Publishes the GitHub Release.
6. Updates `bucket/etwsnap.json` on the default branch for stable versions.

Prerelease tags such as `v0.2.0-preview.1` create prerelease assets but do not update Scoop.

## Verify

After the workflow completes:

```powershell
gh release download v0.1.0
Get-FileHash .\etwsnap-v0.1.0-win-x64.zip -Algorithm SHA256
gh attestation verify .\etwsnap-v0.1.0-win-x64.zip --repo bgn64/etwsnap
```

Then test Scoop from a clean installation:

```powershell
scoop bucket add etwsnap https://github.com/bgn64/etwsnap
scoop install etwsnap
etwsnap --version
scoop update etwsnap
```

Release binaries are intentionally unsigned. Users may see SmartScreen or enterprise-policy warnings.