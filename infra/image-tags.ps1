#Requires -Version 7.4
<#
.SYNOPSIS
    The tags an image gets for the pushed ref (#1044), one per line; nothing when the ref builds no image.
.DESCRIPTION
    - refs/heads/main: the short commit SHA (what the chart's image.tag is set to) and latest (the chart's default tag).
    - refs/tags/v*: the short commit SHA and the tag name. Never latest: a tag may be an old commit.
    - refs/heads/image/*: the short SHA only. A branch asks for an image by being named so; every branch would fill the registry,
      and workflow_dispatch is not available on a mirror's push.
    - Anything else: no tags, so no image.
#>
param(
    [Parameter(Mandatory)] [string] $Ref,
    [Parameter(Mandatory)] [string] $Sha
)
$ErrorActionPreference = 'Stop'
if ($Sha -notmatch '^[0-9a-f]{40}$') { throw "Not a commit id: '$Sha'" }

$short = $Sha.Substring(0, 7)
switch -Regex ($Ref) {
    '^refs/heads/main$' { $short; 'latest' }
    '^refs/tags/(v.+)$' { $short; $Matches[1] }
    '^refs/heads/image/.+$' { $short }
}
