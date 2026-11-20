#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# The image workflows (#1044) cannot run here, so these pin what the design decided and what the chart needs.
BeforeAll {
    $script:root = (git -C $PSScriptRoot rev-parse --show-toplevel)
    $script:reusable = Get-Content (Join-Path $root '.forgejo/workflows/build-image.yml') -Raw
    $script:caller = Get-Content (Join-Path $root '.forgejo/workflows/images.yml') -Raw
    # One block per caller, so every key is read from the caller it belongs to, in any key order.
    $script:callers = [regex]::Matches($caller, '(?ms)^  [\w-]+:\r?\n    uses: .+?(?=^  [\w-]+:\r?\n    uses: |\z)') | ForEach-Object {
        [pscustomobject]@{
            Name = [regex]::Match($_.Value, '(?m)^      name: (\S+)').Groups[1].Value
            Project = [regex]::Match($_.Value, '(?m)^      project: (\S+)').Groups[1].Value
            NeedsNode = $_.Value -match '(?m)^      needs-node: true\s*$'
        }
    }
}

Describe 'the image workflows' {
    It 'build only on main, version tags and image/ branches' {
        $caller | Should -Match "branches: \[main, 'image/\*\*'\]"
        $caller | Should -Match "tags: \['v\*'\]"
    }

    It 'pin the base image by digest' {
        $reusable | Should -Match 'default: mcr\.microsoft\.com/dotnet/aspnet:\d+\.\d+@sha256:[0-9a-f]{64}'
    }

    It 'build for amd64 with PublishContainer, pushing without a Docker daemon' {
        $reusable | Should -Match '-r linux-x64'
        $reusable | Should -Match '/t:PublishContainer'
        $reusable | Should -Not -Match 'docker (login|push|build)'
    }

    It 'report the status context forgejo/image (<name>)' {
        ([regex]::Matches($reusable, 'job: image \(\$\{\{ inputs\.name \}\}\)')).Count | Should -Be 2
    }

    It 'call the reusable workflow once per image and pass every secret it declares' {
        $caller | Should -Match 'uses: \./\.forgejo/workflows/build-image\.yml'
        foreach ($secret in 'GH_STATUS_TOKEN', 'REGISTRY_USER', 'REGISTRY_TOKEN') {
            $caller | Should -Match "$secret`: \`$\{\{ secrets\.$secret \}\}"
        }
    }

    It 'start on exactly the refs that image-tags.ps1 gives a tag' {
        # The triggers of images.yml are the filter and the script is the rule; a ref one accepts and the other refuses is a silent miss or a failed run.
        $tagsScript = Join-Path $root 'infra/image-tags.ps1'
        $sha = '0123456789abcdef0123456789abcdef01234567'
        $branches = [regex]::Match($caller, 'branches: \[(.+?)\]').Groups[1].Value -split ',\s*' | ForEach-Object { $_.Trim("'") }
        $tags = [regex]::Match($caller, 'tags: \[(.+?)\]').Groups[1].Value -split ',\s*' | ForEach-Object { $_.Trim("'") }
        $refs = @($branches | ForEach-Object { 'refs/heads/' + ($_ -replace '\*\*', 'x/y') }) + @($tags | ForEach-Object { 'refs/tags/' + ($_ -replace '\*', '1.0') })
        $refs.Count | Should -Be 3
        foreach ($ref in $refs) { @(& $tagsScript -Ref $ref -Sha $sha).Count | Should -BeGreaterThan 0 -Because "$ref starts images.yml" }
        foreach ($ref in 'refs/heads/other', 'refs/heads/imagery', 'refs/tags/release-1') { @(& $tagsScript -Ref $ref -Sha $sha).Count | Should -Be 0 }
    }

    It 'name the TMS API image after the Service the chart calls it by' {
        $caller | Should -Match 'name: tms-api'
        (Get-Content (Join-Path $root 'deploy/helm/norvia/templates/partner-api.yaml') -Raw) | Should -Match '"name" "tms-api"'
    }
}

Describe 'the images of the APIs and the migrations' {
    BeforeAll {
        $script:chartMigrations = [regex]::Matches((Get-Content (Join-Path $root 'deploy/helm/norvia/values.yaml') -Raw), 'image: (?<image>[\w-]+-migrations)\b') | ForEach-Object { $_.Groups['image'].Value }
    }

    It 'builds, for every caller, the project the image is named after, and the project exists' {
        # tms-api builds Norvia.Tms.Api and driver-app-migrations builds Norvia.DriverApp.Migrations: the name without its dashes is the project's file name without "Norvia." and its dots.
        $callers.Count | Should -BeGreaterThan 0
        foreach ($c in $callers) {
            $expected = ([IO.Path]::GetFileNameWithoutExtension($c.Project) -replace '^Norvia\.', '' -replace '\.', '')
            ($c.Name -replace '-', '') | Should -BeExactly $expected.ToLowerInvariant() -Because "$($c.Name) builds $($c.Project)"
            (Join-Path $root $c.Project) | Should -Exist -Because "$($c.Name) builds $($c.Project)"
        }
    }

    It 'has a caller for every migrations image the chart names' {
        $chartMigrations | Should -Not -BeNullOrEmpty
        foreach ($image in $chartMigrations) { $callers.Name | Should -Contain $image }
    }

    It 'has a caller for each API the chart calls by Service name' {
        foreach ($api in 'tms-api', 'erp-api', 'driver-app-api') { $callers.Name | Should -Contain $api }
    }
    It 'reads every caller of images.yml, so a changed key order cannot hide one from these tests' {
        # tms-api plus those of #1045 and #1046; the regex above drops a caller whose keys are in another order.
        $callers.Count | Should -Be ([regex]::Matches($caller, '(?m)^    uses: \./\.forgejo/workflows/build-image\.yml').Count)
    }

    It 'has no two callers with one name' {
        ($callers.Name | Select-Object -Unique).Count | Should -Be $callers.Count
    }

    It 'containerizes Worker projects too, which the SDK does not do by itself' {
        $reusable | Should -Match '-p:EnableSdkContainerSupport=true'
    }

    It 'names every migrations image of the chart under the registry' -Skip:(-not (Get-Command helm -ErrorAction SilentlyContinue)) {
        $out = helm template norvia (Join-Path $root 'deploy/helm/norvia') --set image.registry=git.home.arpa/smirking_squiggly --set image.tag=0123456 --set edge.baseDomain=norvia.test 2>&1 | Out-String
        foreach ($image in $chartMigrations) { $out | Should -Match "image: git\.home\.arpa/smirking_squiggly/$image`:0123456" }
    }
}

Describe 'the images of the Blazor web apps' {
    BeforeAll {
        $script:webCallers = @($callers | Where-Object { $_.Project -match '\.Web\.csproj$' })
    }

    It 'has a caller for every Web project of the repository' {
        $onDisk = @(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter 'Norvia.*.Web.csproj' | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) -replace '\\', '/' } | Sort-Object)
        $onDisk.Count | Should -BeGreaterThan 0
        @($webCallers.Project | Sort-Object) | Should -Be $onDisk -Because 'a Web project without a caller has no image'
    }

    It 'names the web images the way the chart calls its Services' {
        # The chart reaches partner-portal-web, integration-console-web and health-check-web by these names (workers.yaml, agent-mcp.yaml, values.yaml).
        foreach ($name in 'partner-portal-web', 'integration-console-web', 'health-check-web') { $webCallers.Name | Should -Contain $name }
    }

    It 'turns Node on for exactly the Web projects that build a Tailwind stylesheet' {
        foreach ($w in $webCallers) {
            $package = Join-Path (Split-Path (Join-Path $root $w.Project)) 'package.json'
            $tailwind = (Test-Path $package) -and ((Get-Content $package -Raw) -match '"build:css"')
            $w.NeedsNode | Should -Be $tailwind -Because "$($w.Name) $(if ($tailwind) { 'builds' } else { 'does not build' }) a stylesheet with npm"
        }
    }

    It 'keeps node_modules out of the published content of the projects that build a stylesheet' {
        foreach ($w in $webCallers | Where-Object NeedsNode) {
            (Get-Content (Join-Path $root $w.Project) -Raw) | Should -Match 'Content Remove="node_modules/\*\*' -Because "$($w.Name) must not ship node_modules"
        }
    }
}

Describe 'the chart naming the image' {
    It 'names <registry>/<name>:<sha> for a chart image when helm is given the registry and the SHA' -Skip:(-not (Get-Command helm -ErrorAction SilentlyContinue)) {
        $chart = Join-Path $root 'deploy/helm/norvia'
        $out = helm template norvia $chart --set image.registry=git.home.arpa/smirking_squiggly --set image.tag=0123456 --set edge.baseDomain=norvia.test 2>&1 | Out-String
        $out | Should -Match 'image: git\.home\.arpa/smirking_squiggly/partner-api:0123456'
    }
}
