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

    It 'name the TMS API image after the Service the chart calls it by' {
        $caller | Should -Match 'name: tms-api'
        (Get-Content (Join-Path $root 'deploy/helm/norvia/templates/partner-api.yaml') -Raw) | Should -Match '"name" "tms-api"'
    }
}

Describe 'the chart naming the image' {
    It 'names <registry>/<name>:<sha> for a chart image when helm is given the registry and the SHA' -Skip:(-not (Get-Command helm -ErrorAction SilentlyContinue)) {
        $chart = Join-Path $root 'deploy/helm/norvia'
        $out = helm template norvia $chart --set image.registry=git.home.arpa/smirking_squiggly --set image.tag=0123456 --set edge.baseDomain=norvia.test 2>&1 | Out-String
        $out | Should -Match 'image: git\.home\.arpa/smirking_squiggly/partner-api:0123456'
    }
}
