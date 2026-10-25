using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Application.Operators;

namespace Norvia.HealthCheck.Application.UnitTests.Agents;

/// <summary>The role names are the app roles the estate's Entra registrations define (#371, #392); a rename here must be a rename there.</summary>
public sealed class RolesAndOptionsTests
{
    [Fact]
    public void TheOperatorRoles_AreRunAndRead()
    {
        OperatorRoles.Run.Should().Be("HealthCheck.Run");
        OperatorRoles.Read.Should().Be("HealthCheck.Read");
    }

    [Fact]
    public void TheAgentsOneRole_IsTheCallRole_AndNoOperatorRole()
    {
        AgentRoles.Read.Should().Be("HealthCheck.Agent.Read");
        new[] { OperatorRoles.Run, OperatorRoles.Read }.Should().NotContain(AgentRoles.Read);
    }

    [Fact]
    public void TheAgents_AreOffByDefault() =>
        new AgentsOptions().Enabled.Should().BeFalse();
}
