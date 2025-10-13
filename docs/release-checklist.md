# dnvm Release Checklist

This document provides a comprehensive checklist for creating a new release of dnvm.

## Pre-Release Verification

1. **Check the current latest released version** at https://github.com/dn-vm/dnvm/releases

2. **Check the current package version** in `Directory.Build.props` (look for the `<SemVer>` tag)

3. **If the version is not higher, bump the patch version by one** (or minor/major as appropriate)

4. **Create a new commit with the new version if necessary and create a PR with that change**:
   ```bash
   # Edit Directory.Build.props and README.md with new version
   git add Directory.Build.props README.md
   git commit -m "Bump version to X.Y.Z"
   git push origin <your-branch>
   # Create PR to main branch on GitHub
   ```

5. **Wait for PR review and approval**, ensure all CI tests pass

6. **Merge the PR to main**

7. **Pull the latest main branch locally**:
   ```bash
   git checkout main
   git pull origin main
   ```

## Release Process

8. **Ensure all CI tests are passing** on the main branch - check https://github.com/dn-vm/dnvm/actions

9. **Create and push a git tag** for the new version:
   ```bash
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

10. **Wait for the GitHub Actions workflow to complete** - The `publish.yml` workflow will automatically:
    - Build for all platforms (linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64)
    - Create build artifacts
    - Sign the releases
    - Create a draft release on GitHub

## Post-Release Tasks

11. **Review the draft release** on GitHub - check that all artifacts are present:
    - `dnvm-X.Y.Z-linux-x64.tar.gz` (and `.sig`)
    - `dnvm-X.Y.Z-linux-arm64.tar.gz` (and `.sig`)
    - `dnvm-X.Y.Z-win-x64.zip` (and `.sig`)
    - `dnvm-X.Y.Z-osx-x64.tar.gz` (and `.sig`)
    - `dnvm-X.Y.Z-osx-arm64.tar.gz` (and `.sig`)
    - `relkeys.pub` and `relkeys.pub.sig`

12. **Write release notes** summarizing changes, new features, bug fixes, and breaking changes

13. **Publish the release** by changing it from draft to published

14. **Verify the website update workflow triggered** - The `webUpdate.yml` workflow should dispatch an event to update dn-vm.github.io

15. **Test installation** from the new release on at least one platform:
    ```bash
    # Download and test the new release
    curl -L https://github.com/dn-vm/dnvm/releases/download/vX.Y.Z/dnvm-X.Y.Z-<platform>.tar.gz -o dnvm.tar.gz
    ```

16. **Announce the release** (if applicable) on relevant channels/social media

## Notes

- The publish workflow is triggered by pushing a tag starting with `v` (e.g., `v0.9.9`)
- Releases are created as drafts automatically, allowing for review before publishing
- The workflow handles signing with the private key stored in GitHub secrets
- Cross-compilation for ARM64 Linux is handled automatically in the workflow
- **Important**: Tags should only be created from the `main` branch after the version bump PR is merged

## Version Numbering

dnvm follows semantic versioning (semver):
- **Major version** (X.0.0): Breaking changes or major new features
- **Minor version** (0.X.0): New features, backward compatible
- **Patch version** (0.0.X): Bug fixes, backward compatible
