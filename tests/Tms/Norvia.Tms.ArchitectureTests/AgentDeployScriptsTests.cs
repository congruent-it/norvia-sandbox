namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The scripts that give a published Agent its connection and role (#499): the connection authenticates as the Agent, the role
/// goes to the Agent's identity and never to the project's, and no script prints a token or a secret. The scripts run in Azure;
/// these read them.
/// </summary>
public sealed class AgentDeployScriptsTests
{
    [Fact]
    public void Connection_AuthenticatesAsTheAgent_NeverAsTheProject()
    {
        var script = Script("create-agent-connection.sh");

        script.Should().Contain("--auth-type agentic-identity").And.Contain("--audience");
        script.Should().NotContain("project-managed-identity", "the project's shared identity holds no roles");
    }

    [Fact]
    public void RoleAssignment_GoesToTheAgentIdentity_AndChecksTheProjectIdentityHoldsNone()
    {
        var script = Script("assign-agent-roles.sh");

        script.Should().Contain("appRoleAssignedTo").And.Contain("agentIdentityId");
        script.Should().Contain("the project's shared identity").And.Contain("must hold none");
    }

    [Fact]
    public void RoleAssignment_UsesTheDeploymentRegistration_OnlyWhenOneIsGiven()
    {
        var script = Script("assign-agent-roles.sh");

        script.Should().Contain("AGENT_ROLES_CLIENT_ID").And.Contain("az account get-access-token --resource-type ms-graph");
        script.Should().Contain("${AGENT_ROLES_CLIENT_SECRET:?", "a secret is read from the environment, never written into the script");
    }

    [Theory]
    [InlineData("create-agent-connection.sh")]
    [InlineData("assign-agent-roles.sh")]
    public void Scripts_NeverEchoATokenOrASecret(string name)
    {
        var lines = Script(name).Split('\n').Where(line => line.TrimStart().StartsWith("echo", StringComparison.Ordinal));

        lines.Should().NotContain(line => line.Contains("$token", StringComparison.Ordinal) || line.Contains("SECRET", StringComparison.Ordinal));
    }

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", name));
}
