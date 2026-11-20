# Which application workflow a change affects (#1079). Read by .forgejo/actions/scope/scope.ps1, which every application workflow runs first.
# The tests of .forgejo/actions/scope check this file against the project references and the folders of src/ and tests/.
#
# Rules (scope.ps1): a file affects the applications whose Paths or Shared entry match it; a file nothing claims is skipped only when Ignored matches it,
# and otherwise affects every application. So infra/, contracts/, seed/, agents/, evaluation/, deploy/, Directory.*.props, global.json and a new
# top-level folder need no entry: a change to them runs everything, which is the point of "shared code".
# Paths are prefix globs, * crosses folders. The key of an application is its workflow file (.forgejo/workflows/<key>.yml).
#
# What an application's tests exercise is more than it references: the AppHost starts the whole estate. A change in another application's code
# runs that application's workflow only; main runs every workflow, which catches what a cross-application change breaks.
@{
    Applications = @{
        'tms'                = @('src/Tms/*', 'tests/Tms/*', '.forgejo/workflows/tms.yml')
        'partner-portal'     = @('src/PartnerPortal/*', 'tests/PartnerPortal/*', '.forgejo/workflows/partner-portal.yml')
        'erp'                = @('src/Erp/*', 'tests/Erp/*', '.forgejo/workflows/erp.yml')
        'driver-app'         = @('src/DriverApp/*', 'tests/DriverApp/*', '.forgejo/workflows/driver-app.yml')
        'simulator'          = @('src/Simulator/*', 'tests/Simulator/*', '.forgejo/workflows/simulator.yml')
        'integration-console' = @('src/IntegrationConsole/*', 'tests/IntegrationConsole/*', '.forgejo/workflows/integration-console.yml')
        'integration'        = @('src/Integration/*', 'tests/Integration/*', '.forgejo/workflows/integration.yml')
        'health-check'       = @('src/HealthCheck/*', 'tests/HealthCheck/*', '.forgejo/workflows/health-check.yml')
    }

    # Code more than one application builds or reads: the folders the other applications reference, and what their tests read from the repository.
    Shared = @(
        @{ Paths = @('src/Norvia.ServiceDefaults/*', 'tests/ServiceDefaults/*', 'src/Norvia.AppHost/*')
           Applications = @('tms', 'partner-portal', 'erp', 'driver-app', 'simulator', 'integration-console', 'integration', 'health-check') }
        @{ Paths = @('src/Norvia.Branding/*')
           Applications = @('tms', 'partner-portal', 'erp', 'driver-app', 'simulator', 'integration-console', 'health-check') }
        @{ Paths = @('src/Norvia.Agents/*')
           Applications = @('tms', 'partner-portal', 'erp', 'simulator', 'integration-console', 'integration', 'health-check') }

        # The tests of an application that reference another application's code (project references, outside the AppHost).
        @{ Paths = @('src/Integration/*'); Applications = @('erp', 'health-check', 'integration-console') }
        @{ Paths = @('src/IntegrationConsole/*'); Applications = @('partner-portal') }
        @{ Paths = @('src/Erp/*', 'src/Tms/*', 'tests/Erp/*', 'tests/Tms/*', 'src/Norvia.Branding/*'); Applications = @('integration') }

        # Documents the architecture and evaluation tests read.
        @{ Paths = @('docs/architecture/*', 'docs/processes/*'); Applications = @('tms') }
        @{ Paths = @('docs/operations/agent-evaluation.md'); Applications = @('tms', 'erp', 'integration', 'simulator') }
    )

    # No application workflow reads these. The repo-level workflow (ci.yml) still runs on them: the docs generator, Branding and Docs unit tests, formatting.
    Ignored = @(
        'docs/*', 'tests/Branding/*', 'tests/Docs/*', 'src/Docs/*', 'src/Norvia.AppHost.KubeSpike/*'
        '.github/*', '.devcontainer/*', '.forgejo/workflows/*'
        'README.md', 'CONTRIBUTING.md', 'GLOSSARY.md', 'SCENARIO.md', 'CLAUDE.md'
    )
}
