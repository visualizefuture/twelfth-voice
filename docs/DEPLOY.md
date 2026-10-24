# Deploying Twelfth Voice to Azure

One container on Azure Container Apps serves everything: the fan app, the REST API, the SignalR
live channel and the MCP server. It signs in to Microsoft Foundry and Azure SQL with a managed
identity, so the only key it holds is the Azure AI Speech key (kept as a Container Apps secret).

```
GitHub (merge to master)
  └─ Actions: build image ─► Azure Container Registry ─► Container Apps (twelfth-voice, 1 replica)
                                                            ├─ Microsoft Foundry (gpt-4.1-mini)   managed identity
                                                            ├─ Azure SQL Database (free offer)     managed identity
                                                            └─ Azure AI Speech                     key in a secret
```

## One-time setup (about 15 minutes)

1. Open **Cloud Shell** (Bash) in the Azure portal.
2. Upload `deploy/azure-setup.sh` (Manage files > Upload) and run:
   ```bash
   bash azure-setup.sh
   ```
   It finds your Foundry and Speech resources in `rg-twelfth-voice`, creates the rest, and is safe to run again.
3. Add the three values it prints as **repository secrets** in GitHub
   (Settings > Secrets and variables > Actions): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
4. Run **Deploy to Azure** from the Actions tab (or merge to `master`). It builds, rolls out and smoke-tests the app,
   and prints its URL.

## What it costs

| Piece | Plan | Roughly |
|---|---|---|
| Container Apps | 0.5 vCPU, 1 GiB, one always-on replica | $15–20 / month |
| Container Registry | Basic | $5 / month |
| Azure SQL Database | Serverless, free offer (auto-pauses at the free limit) | $0 |
| Foundry gpt-4.1-mini | Pay per token, capped by `AzureAI:MaxCallsPerHour` | a few dollars |
| Azure AI Speech | F0 free tier, clips cached | $0 |

After judging, set the app to scale to zero (`az containerapp update -n twelfth-voice -g rg-twelfth-voice --min-replicas 0`)
to bring the cost close to nothing. Matches live in memory, so it keeps one replica while running.

## Configuration in Azure

| Setting | Value |
|---|---|
| `AZURE_CLIENT_ID` | the managed identity's client ID |
| `ConnectionStrings__TwelfthVoice` | Azure SQL, `Authentication=Active Directory Managed Identity` |
| `AzureAI__Endpoint` | `https://<foundry>.services.ai.azure.com/openai/v1/` (no key: Entra ID) |
| `Speech__Key` / `Speech__Region` | secret reference / `eastus2` |
