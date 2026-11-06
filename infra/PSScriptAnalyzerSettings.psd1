# PSScriptAnalyzer settings for every .ps1 and .psm1 in the repo (infra/test-scripts.ps1, CI). Errors fail the build.
@{
    Severity     = @('Error', 'Warning')
    ExcludeRules = @(
        # The scripts report to the console by design (PASS / FAIL lines, the URLs to open).
        'PSAvoidUsingWriteHost'
    )
}
