#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# The scope of a CI workflow (#1079): which application workflows a change affects. Seam: the script run with the changed files (or a git base) and a manifest.
BeforeAll {
    $script:script = Join-Path $PSScriptRoot 'scope.ps1'
    $script:root = (git -C $PSScriptRoot rev-parse --show-toplevel)
    $script:fixture = Join-Path $TestDrive 'ci-scope.psd1'
    Set-Content $script:fixture @'
@{
    Applications = @{
        alpha = @('src/Alpha/*', 'tests/Alpha/*')
        beta  = @('src/Beta/*', 'tests/Beta/*')
    }
    Shared = @(
        @{ Paths = @('src/Common/*'); Applications = @('alpha', 'beta') }
        @{ Paths = @('src/Alpha.Only/*'); Applications = @('alpha') }
    )
    Ignored = @('docs/*', 'README.md')
}
'@
    function script:Scope($Application, $Files, [switch] $RunAll) {
        & $script:script -Application $Application -ChangedFile $Files -Manifest $script:fixture -RunAll:$RunAll
    }
}

Describe 'scope' {
    It 'is affected by a change under its own source or tests' -ForEach @(
        @{ File = 'src/Alpha/Api/Program.cs' }, @{ File = 'tests/Alpha/Api.UnitTests/A.cs' }
    ) {
        Scope 'alpha' @($File) | Should -BeTrue
        Scope 'beta' @($File) | Should -BeFalse
    }

    It 'is affected by shared code only when it consumes it' {
        Scope 'beta' @('src/Common/X.cs') | Should -BeTrue
        Scope 'beta' @('src/Alpha.Only/X.cs') | Should -BeFalse
        Scope 'alpha' @('src/Alpha.Only/X.cs') | Should -BeTrue
    }

    It 'ignores paths no application reads' {
        Scope 'alpha' @('docs/adr/0001.md', 'README.md') | Should -BeFalse
    }

    It 'runs everything for a path nothing claims, so a new top-level folder cannot be skipped' {
        Scope 'alpha' @('infra/estate.bicep') | Should -BeTrue
        Scope 'beta' @('global.json') | Should -BeTrue
    }

    It 'is affected when any one of several files affects it' {
        Scope 'alpha' @('docs/a.md', 'src/Alpha/A.cs') | Should -BeTrue
    }

    It 'is always affected under -RunAll, as on main' {
        Scope 'alpha' @('docs/a.md') -RunAll | Should -BeTrue
    }

    It 'is affected when nothing changed is known (no files)' {
        Scope 'alpha' @() | Should -BeTrue
    }

    It 'fails for an application the manifest does not know' {
        { Scope 'gamma' @('src/Alpha/A.cs') } | Should -Throw '*gamma*'
    }

    It 'matches * across folders, as a prefix glob' {
        Scope 'alpha' @('src/Alpha/deep/er/file.cs') | Should -BeTrue
    }
}

Describe 'scope from git' {
    BeforeAll {
        $script:repo = Join-Path $TestDrive 'repo'
        git init -q -b main $script:repo
        git -C $script:repo config user.email t@example.com
        git -C $script:repo config user.name t
        New-Item -ItemType Directory (Join-Path $script:repo 'docs') | Out-Null
        Set-Content (Join-Path $script:repo 'docs/a.md') 'a'
        git -C $script:repo add -A; git -C $script:repo commit -q -m base
        git -C $script:repo checkout -q -b feature
    }

    It 'diffs the head against the merge-base with the base branch' {
        Set-Content (Join-Path $script:repo 'docs/b.md') 'b'
        git -C $script:repo add -A; git -C $script:repo commit -q -m docs
        & $script:script -Application alpha -Manifest $script:fixture -Base main -Repository $script:repo | Should -BeFalse

        New-Item -ItemType Directory (Join-Path $script:repo 'src/Alpha') -Force | Out-Null
        Set-Content (Join-Path $script:repo 'src/Alpha/A.cs') 'x'
        git -C $script:repo add -A; git -C $script:repo commit -q -m code
        & $script:script -Application alpha -Manifest $script:fixture -Base main -Repository $script:repo | Should -BeTrue
        & $script:script -Application beta -Manifest $script:fixture -Base main -Repository $script:repo | Should -BeFalse
    }

    It 'runs everything when the base cannot be resolved' {
        & $script:script -Application beta -Manifest $script:fixture -Base origin/nothing -Repository $script:repo | Should -BeTrue
    }
}

# The job log is where a run that was not skipped has to explain itself.
Describe 'scope explains its answer' {
    BeforeAll {
        function script:Said($Application, $Files, [switch] $RunAll) {
            $out = & $script:script -Application $Application -ChangedFile $Files -Manifest $script:fixture -RunAll:$RunAll 6>&1
            ($out | Where-Object { $_ -is [System.Management.Automation.InformationRecord] } | ForEach-Object { "$($_.MessageData)" }) -join "`n"
        }
    }

    It 'lists the changed files and names the file and the rule that affect the application' {
        $said = Said alpha @('docs/a.md', 'src/Alpha/A.cs')
        $said | Should -Match 'Changed files: 2'
        $said | Should -Match 'docs/a\.md'
        $said | Should -Match 'alpha: affected by src/Alpha/A\.cs \(its own paths\)'
    }

    It 'names shared code, an unclaimed path, -RunAll and an empty change as the reason' {
        Said beta @('src/Common/C.cs') | Should -Match 'affected by src/Common/C\.cs \(shared code it consumes\)'
        Said beta @('newfolder/x.txt') | Should -Match 'affected by newfolder/x\.txt \(no application, shared entry or ignored path claims it'
        Said beta @('docs/a.md') -RunAll | Should -Match 'every workflow runs on this ref'
        Said beta @() | Should -Match 'no changed file is known'
    }

    It 'says not affected when it skips' {
        Said beta @('src/Alpha/A.cs') | Should -Match 'beta: not affected'
    }
}
# The real manifest: the rules that keep the path lists honest as the code changes.
Describe 'the manifest of this repository' {
    BeforeAll {
        $script:real = Join-Path $script:root '.forgejo/ci-scope.psd1'
        $script:apps = (Import-PowerShellDataFile $script:real).Applications.Keys
        function script:Affects($Application, $File) {
            & $script:script -Application $Application -ChangedFile @($File) -Manifest $script:real
        }
        # Top-level folder of src/ or tests/ from a project path.
        function script:Owner($Path) { ($Path -split '/')[0..1] -join '/' }
    }

    It 'has a workflow file for every application' {
        foreach ($app in $script:apps) { Join-Path $script:root ".forgejo/workflows/$app.yml" | Should -Exist }
    }

    It 'runs the workflow of an application when its own workflow file changes' {
        foreach ($app in $script:apps) { Affects $app ".forgejo/workflows/$app.yml" | Should -BeTrue }
    }

    It 'names every top-level folder of src and tests as an application, shared code or ignored' {
        $manifest = Import-PowerShellDataFile $script:real
        $patterns = @($manifest.Applications.Values) + @($manifest.Shared | ForEach-Object { $_.Paths }) + @($manifest.Ignored) | ForEach-Object { $_ }
        $unclaimed = foreach ($dir in (Get-ChildItem (Join-Path $script:root 'src'), (Join-Path $script:root 'tests') -Directory)) {
            $probe = "$($dir.Parent.Name)/$($dir.Name)/probe.cs"
            if (-not ($patterns | Where-Object { $probe -like $_ })) { $probe }
        }
        $unclaimed | Should -BeNullOrEmpty -Because 'a folder the manifest does not name makes every change to it run every workflow'
    }
    It 'runs the workflow of every application that references the project a change is in' {
        $missing = @()
        $projects = Get-ChildItem (Join-Path $script:root 'src'), (Join-Path $script:root 'tests') -Recurse -Filter *.csproj |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        foreach ($project in $projects) {
            $from = Owner ([IO.Path]::GetRelativePath($script:root, $project.FullName) -replace '\\', '/')
            foreach ($m in [regex]::Matches((Get-Content $project.FullName -Raw), 'ProjectReference Include="([^"]+)"')) {
                $target = [IO.Path]::GetRelativePath($script:root, [IO.Path]::GetFullPath((Join-Path $project.DirectoryName ($m.Groups[1].Value -replace '\\', '/')))) -replace '\\', '/'
                $to = Owner $target
                # The AppHost references every application to run it; its tests are scoped by what they exercise, not by this edge.
                if ($to -eq $from -or $to -like 'src/Norvia.AppHost*' -or $from -like 'src/Norvia.AppHost*') { continue }
                $consumers = @($script:apps | Where-Object { Affects $_ "$from/probe.cs" })
                foreach ($consumer in $consumers) {
                    if (-not (Affects $consumer "$to/probe.cs")) { $missing += "$to is referenced from $from, which affects $consumer, but a change in $to does not" }
                }
            }
        }
        $missing | Sort-Object -Unique | Should -BeNullOrEmpty
    }
}
