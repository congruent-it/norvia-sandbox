# Bicep owns the estate, Aspire owns the apps

Infrastructure is split into two layers. Hand-written Bicep in `infra/` owns the AIS estate: Service Bus, API Management, Logic Apps, Key Vault, Entra app registrations and monitoring, including every broken/fixed toggle. Aspire owns the applications (TMS, Partner portal), their databases and the Container Apps environment, and it references the existing Service Bus instead of creating it. azd runs the estate Bicep as a pre-provision hook and then deploys the Aspire apps. We kept the estate as plain Bicep because it is the part that audits and demos are about: its toggles must stay findable by grep, in the same style as the health-check samples. Aspire-generated infrastructure would bury them.

## Considered Options

- **Aspire owns everything**, pulling the estate Bicep into the AppHost as template resources: a single deployment model, but it ties the estate's shape to Aspire's resource model, and APIM and Logic Apps Standard have no first-class support.
