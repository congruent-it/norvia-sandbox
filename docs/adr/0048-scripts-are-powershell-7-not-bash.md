# Scripts are PowerShell 7, not bash

The repo's scripts (54 in `infra/`, plus `src/Norvia.Branding/export-icons.sh`) are written in PowerShell 7 (`pwsh`), not bash. They are being converted one slice at a time, each `.sh` deleted as its `.ps1` lands, so there is never a second copy. `export-icons` keeps `rsvg-convert` and `magick` as prerequisites.

Bash scripts ran only where bash and its companions are: Windows needed WSL or Git Bash, and the scripts leaned on `jq`, `zip`, `python3`, `sed` and `mktemp --suffix`, whose flags differ between GNU and BSD. PowerShell 7 runs the same on Windows, Linux and macOS and brings what those tools were there for: `ConvertFrom-Json`/`ConvertTo-Json`, `Compress-Archive` and `Invoke-RestMethod`. The Azure CLI stays, as does `azd`; `azure.yaml` runs its hooks with `shell: pwsh`.

The rules:

- Every script starts with `#Requires -Version 7.4` and `$ErrorActionPreference = 'Stop'`. File names keep their kebab-case; only the extension changes.
- The helpers the scripts repeated (`pass`/`fail`, the app FQDN, the throwaway Container Apps job with its cleanup, Log Analytics queries, `until_found`) live once, in `infra/lib/Norvia.Infra.psm1`, with `Verb-Noun` names (`Write-Pass`, `Write-Fail`, `Get-AppFqdn`, `New-ThrowawayJob`, `Invoke-LogAnalyticsQuery`, `Wait-Until`). Every `az` call in the module goes through `Invoke-Az`, which fails on a non-zero exit code. A script imports the module and does not copy a helper.
- CI parses every tracked `.ps1` and `.psm1`, runs PSScriptAnalyzer (`infra/PSScriptAnalyzerSettings.psd1`; an error fails the build) and the Pester tests, with `infra/test-scripts.ps1`. The module's Pester tests mock `az`. The scripts that call Azure are checked by hand after a deployment, as before.
- The architecture tests that read script text read the `.ps1` files and match the PowerShell form of each rule.

## Considered Options

- **Keep bash and require WSL on Windows:** no work, but every Windows contributor and the presenter on a Windows laptop carries a second environment, and the scripts keep depending on `jq`, `zip` and `python3`.
- **Move the logic into a .NET tool:** one language, testable like the rest. It is a bigger rewrite than the scripts warrant, and an `az` wrapper in C# is more code than the same wrapper in PowerShell.
- **Python:** the scripts already embed Python, but it needs an interpreter and a virtual environment on every machine, and `az` calls are clumsier than in a shell.

## Consequences

- Linux and macOS users must install `pwsh` (the README prerequisites say so). `jq` and `zip` leave the prerequisites as the scripts that need them are converted.
- During the conversion some scripts are bash and some PowerShell, so the README names each by its extension.
- Quoting for native commands differs from bash: a JSON body is passed to `az` as a `@file`, not as a string argument, so it is the same on every shell.
- The module's tests cover the helpers, not the Azure calls around them.
