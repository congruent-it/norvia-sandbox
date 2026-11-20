#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# The image workflows (#1044) cannot run here, so these pin what the design decided and what the chart needs.
BeforeAll {
    $script:root = (git -C $PSScriptRoot rev-parse --show-toplevel)
    $script:reusable = Get-Content (Join-Path $root '.forgejo/workflows/build-image.yml') -Raw
    $script:caller = Get-Content (Join-Path $root '.forgejo/workflows/images.yml') -Raw
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
        # One caller per image: name and project, read from images.yml.
        $script:callers = [regex]::Matches($caller, '(?m)^  [\w-]+:\r?\n    uses: .+\r?\n    with:\r?\n      project: (?<project>\S+)\r?\n      name: (?<name>\S+)') |
            ForEach-Object { [pscustomobject]@{ Name = $_.Groups['name'].Value; Project = $_.Groups['project'].Value } }
        $script:chartMigrations = [regex]::Matches((Get-Content (Join-Path $root 'deploy/helm/norvia/values.yaml') -Raw), 'image: (?<image>[\w-]+-migrations)\b') | ForEach-Object { $_.Groups['image'].Value }
    }

    It 'has a caller for <_>, the image the chart names, building the project of that name' -ForEach 'tms-api', 'erp-api', 'driver-app-api', 'tms-migrations', 'erp-migrations', 'driver-app-migrations', 'partner-portal-migrations', 'simulator-migrations' {
        $found = $callers | Where-Object Name -eq $_
        $found | Should -Not -BeNullOrEmpty
        # tms-api -> Norvia.Tms.Api, driver-app-migrations -> Norvia.DriverApp.Migrations
        $words = @($_ -split '-' | ForEach-Object { (Get-Culture).TextInfo.ToTitleCase($_) })
        $kind = $words[-1]
        $app = $words[0..($words.Count - 2)] -join ''
        $found.Project | Should -Match "/Norvia\.$app\.$kind/Norvia\.$app\.$kind\.csproj$"
        (Join-Path $root $found.Project) | Should -Exist
    }

    It 'covers every migrations image the chart names' {
        $chartMigrations.Count | Should -Be 5
        foreach ($image in $chartMigrations) { $callers.Name | Should -Contain $image }
    }

    It 'reads every caller of images.yml, so a changed key order cannot hide one from these tests' {
        # tms-api plus the eight of #1045; the regex above drops a caller whose keys are in another order.
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

Describe 'the chart naming the image' {
    It 'names <registry>/<name>:<sha> for a chart image when helm is given the registry and the SHA' -Skip:(-not (Get-Command helm -ErrorAction SilentlyContinue)) {
        $chart = Join-Path $root 'deploy/helm/norvia'
        $out = helm template norvia $chart --set image.registry=git.home.arpa/smirking_squiggly --set image.tag=0123456 --set edge.baseDomain=norvia.test 2>&1 | Out-String
        $out | Should -Match 'image: git\.home\.arpa/smirking_squiggly/partner-api:0123456'
    }
}
