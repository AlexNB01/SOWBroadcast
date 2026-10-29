# Code signing with SignPath Foundation

The release workflow (`.github/workflows/release.yml`) signs the installer with a free certificate from the
[SignPath Foundation](https://signpath.org/). Until the steps below are done the workflow still works; it just publishes an
unsigned installer (Windows SmartScreen warns about those).

## One-time setup (the owner does this; it needs a SignPath account)

1. **Apply** for the free open-source program at signpath.org (Foundation → apply). The project qualifies on paper: public repository,
   MIT licence, built on GitHub-hosted runners. They review the application by hand, so it can take a while. Use the repository
   `https://github.com/AlexNB01/SOWBroadcast` and point them to the *Code signing policy* section of the README.
2. In SignPath create the **project** with the slug `sowbroadcast` (the workflow uses that slug) and add a signing policy with the
   slug `release-signing` (approval by the owner is the sensible choice: every signing request is approved by hand).
3. **Artifact configuration**: paste `artifact-configuration.xml` from this folder into the project and make it the default. It signs
   `SOWBroadcast-Setup-*.exe` inside the ZIP the workflow uploads.
4. **Trusted build system**: link the GitHub repository (SignPath's GitHub.com trusted build system) so that SignPath accepts artifacts
   built by this repository's Actions runs.
5. In the GitHub repository (Settings → Secrets and variables → Actions):
   - secret `SIGNPATH_API_TOKEN`: an API token of a SignPath user that may submit signing requests for this project,
   - variable `SIGNPATH_ORGANIZATION_ID`: the organization id shown in SignPath.
6. Publish as usual (tag `v3.x.y`). The workflow builds the installer, hands it to SignPath, waits for the signed copy (approve the
   request in SignPath when it asks), checks that the signature is valid, makes the `.sha256` again from the signed file and attaches both
   to the release.

## What gets signed

Only the setup program (`SOWBroadcast-Setup-<version>.exe`), which is what SmartScreen and the in-program update look at. The files
inside it (the program, its DLLs) and the uninstaller are not signed separately. Signing them as well needs the SignPath artifact
configuration to describe the installer's contents, which Inno Setup would have to produce in an earlier step.

SmartScreen reputation builds up over time for a certificate: the first releases may still show a warning, a little less each time.

## Not verified

The workflow has not been run against SignPath (no account yet). The action name and inputs follow SignPath's documentation for the
GitHub action `signpath/github-action-submit-signing-request@v1`; if SignPath's page for the project suggests different slugs or
inputs, change `project-slug`, `signing-policy-slug` or the artifact configuration to match.
