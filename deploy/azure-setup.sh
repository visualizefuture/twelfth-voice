#!/usr/bin/env bash
# Twelfth Voice: one-time Azure setup. Run it in Azure Cloud Shell (Bash):
#
#   bash azure-setup.sh
#
# It creates, in your existing resource group:
#   - a managed identity for the app (no passwords: it signs in to Foundry and Azure SQL)
#   - a container registry (Basic) for the app's images
#   - a Container Apps environment and the app itself (one always-on replica)
#   - an Azure SQL server and a database on the free offer (Entra-only sign-in)
#   - a GitHub Actions sign-in (OIDC federated credential, no stored secret) that can deploy
# and prints the three values to add to GitHub. Safe to run again: existing pieces are reused.
#
# Override any of these by exporting them before running the script.
set -euo pipefail

RG="${RG:-rg-twelfth-voice}"
LOCATION="${LOCATION:-eastus2}"
APP="${APP:-twelfth-voice}"
ENV_NAME="${ENV_NAME:-cae-twelfth-voice}"
IDENTITY="${IDENTITY:-id-twelfth-voice}"
DB_NAME="${DB_NAME:-twelfthvoice}"
GITHUB_REPO="${GITHUB_REPO:-visualizefuture/twelfth-voice}"
MODEL_DEPLOYMENT="${MODEL_DEPLOYMENT:-gpt-4.1-mini}"

step() { printf '\n\033[1;33m==> %s\033[0m\n' "$*"; }
note() { printf '    %s\n' "$*"; }

SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
SUFFIX=$(printf '%s' "$SUBSCRIPTION_ID$RG" | sha1sum | cut -c1-6)
ACR="${ACR:-twelfthvoice$SUFFIX}"
SQL_SERVER="${SQL_SERVER:-sql-twelfth-voice-$SUFFIX}"

step "Subscription: $(az account show --query name -o tsv) ($SUBSCRIPTION_ID), resource group $RG ($LOCATION)"
note "Wrong one? Run: az account set --subscription <name or id>, then run this script again."
az group show -n "$RG" -o none 2>/dev/null || az group create -n "$RG" -l "$LOCATION" -o none

step "Registering resource providers and the Container Apps CLI extension"
for ns in Microsoft.App Microsoft.OperationalInsights Microsoft.ContainerRegistry Microsoft.Sql Microsoft.ManagedIdentity; do
  az provider register -n "$ns" -o none
done
az extension add -n containerapp --upgrade -y -o none 2>/dev/null || true

step "Finding your Foundry (AI Services) and Speech resources in $RG"
AI_NAME="${AI_NAME:-$(az cognitiveservices account list -g "$RG" --query "[?kind=='AIServices'].name | [0]" -o tsv)}"
SPEECH_NAME="${SPEECH_NAME:-$(az cognitiveservices account list -g "$RG" --query "[?kind=='SpeechServices'].name | [0]" -o tsv)}"
[ -n "$AI_NAME" ] || { echo "No AI Services (Foundry) resource found in $RG. Set AI_NAME=<name> and AI_RG=<group> and rerun."; exit 1; }
AI_RG="${AI_RG:-$RG}"
AI_ID=$(az cognitiveservices account show -n "$AI_NAME" -g "$AI_RG" --query id -o tsv)
AI_ENDPOINT="https://$AI_NAME.services.ai.azure.com/openai/v1/"
note "Foundry: $AI_NAME  ($AI_ENDPOINT)"
if [ -n "$SPEECH_NAME" ]; then
  SPEECH_REGION=$(az cognitiveservices account show -n "$SPEECH_NAME" -g "$RG" --query location -o tsv)
  SPEECH_KEY=$(az cognitiveservices account keys list -n "$SPEECH_NAME" -g "$RG" --query key1 -o tsv)
  note "Speech:  $SPEECH_NAME ($SPEECH_REGION)"
else
  SPEECH_REGION=""; SPEECH_KEY=""
  note "Speech:  none found, the app will run without the goal call"
fi

step "Managed identity $IDENTITY"
az identity show -n "$IDENTITY" -g "$RG" -o none 2>/dev/null || az identity create -n "$IDENTITY" -g "$RG" -l "$LOCATION" -o none
ID_RESOURCE=$(az identity show -n "$IDENTITY" -g "$RG" --query id -o tsv)
ID_CLIENT=$(az identity show -n "$IDENTITY" -g "$RG" --query clientId -o tsv)
ID_PRINCIPAL=$(az identity show -n "$IDENTITY" -g "$RG" --query principalId -o tsv)

assign() { # role, scope, principal id, principal type
  az role assignment create --assignee-object-id "$3" --assignee-principal-type "$4" --role "$1" --scope "$2" -o none 2>/dev/null \
    && note "granted: $1" || note "already granted (or not available): $1"
}

step "Container registry $ACR (Basic)"
az acr show -n "$ACR" -g "$RG" -o none 2>/dev/null || az acr create -n "$ACR" -g "$RG" -l "$LOCATION" --sku Basic --admin-enabled false -o none
ACR_ID=$(az acr show -n "$ACR" -g "$RG" --query id -o tsv)
ACR_SERVER=$(az acr show -n "$ACR" -g "$RG" --query loginServer -o tsv)
assign AcrPull "$ACR_ID" "$ID_PRINCIPAL" ServicePrincipal

step "Letting the app call your model on Foundry without a key"
assign "Cognitive Services OpenAI User" "$AI_ID" "$ID_PRINCIPAL" ServicePrincipal
assign "Azure AI User" "$AI_ID" "$ID_PRINCIPAL" ServicePrincipal

step "Azure SQL server $SQL_SERVER and free database $DB_NAME"
if ! az sql server show -n "$SQL_SERVER" -g "$RG" -o none 2>/dev/null; then
  # Entra-only sign-in, with the app's identity as the administrator: no SQL passwords anywhere.
  az sql server create -n "$SQL_SERVER" -g "$RG" -l "$LOCATION" \
    --enable-ad-only-auth --external-admin-principal-type Application \
    --external-admin-name "$IDENTITY" --external-admin-sid "$ID_CLIENT" -o none
fi
az sql server firewall-rule create -g "$RG" -s "$SQL_SERVER" -n AllowAzureServices \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 -o none 2>/dev/null || true
if ! az sql db show -n "$DB_NAME" -s "$SQL_SERVER" -g "$RG" -o none 2>/dev/null; then
  az sql db create -n "$DB_NAME" -s "$SQL_SERVER" -g "$RG" \
    --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 2 \
    --use-free-limit --free-limit-exhaustion-behavior AutoPause -o none
fi
SQL_CONN="Server=tcp:$SQL_SERVER.database.windows.net,1433;Database=$DB_NAME;Authentication=Active Directory Managed Identity;User Id=$ID_CLIENT;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60"

step "Container Apps environment $ENV_NAME"
az containerapp env show -n "$ENV_NAME" -g "$RG" -o none 2>/dev/null || az containerapp env create -n "$ENV_NAME" -g "$RG" -l "$LOCATION" -o none

ENV_VARS=(
  "ASPNETCORE_ENVIRONMENT=Production"
  "AZURE_CLIENT_ID=$ID_CLIENT"
  "Storage__Provider=SqlServer"
  "ConnectionStrings__TwelfthVoice=$SQL_CONN"
  "AzureAI__Endpoint=$AI_ENDPOINT"
  "AzureAI__Deployment=$MODEL_DEPLOYMENT"
)
SECRETS=()
if [ -n "$SPEECH_KEY" ]; then
  SECRETS+=("speech-key=$SPEECH_KEY")
  ENV_VARS+=("Speech__Region=$SPEECH_REGION" "Speech__Key=secretref:speech-key")
fi

step "Container app $APP (one always-on replica: live matches are held in memory)"
if ! az containerapp show -n "$APP" -g "$RG" -o none 2>/dev/null; then
  # A placeholder image until the first GitHub deploy pushes the real one.
  CREATE_ARGS=(-n "$APP" -g "$RG" --environment "$ENV_NAME"
    --image mcr.microsoft.com/k8se/quickstart:latest --target-port 80 --ingress external
    --min-replicas 1 --max-replicas 1 --cpu 0.5 --memory 1.0Gi
    --user-assigned "$ID_RESOURCE" --registry-server "$ACR_SERVER" --registry-identity "$ID_RESOURCE")
  if [ ${#SECRETS[@]} -gt 0 ]; then CREATE_ARGS+=(--secrets "${SECRETS[@]}"); fi
  az containerapp create "${CREATE_ARGS[@]}" --env-vars "${ENV_VARS[@]}" -o none
else
  if [ ${#SECRETS[@]} -gt 0 ]; then az containerapp secret set -n "$APP" -g "$RG" --secrets "${SECRETS[@]}" -o none; fi
  az containerapp update -n "$APP" -g "$RG" --set-env-vars "${ENV_VARS[@]}" -o none
fi
APP_URL="https://$(az containerapp show -n "$APP" -g "$RG" --query properties.configuration.ingress.fqdn -o tsv)"

step "GitHub Actions sign-in for $GITHUB_REPO (OIDC, no stored secret)"
# A second managed identity trusted by GitHub Actions for this repo's master branch. Unlike an
# app registration it needs no directory permissions, so it also works for guest owners.
DEPLOY_IDENTITY="${DEPLOY_IDENTITY:-id-twelfth-voice-deploy}"
az identity show -n "$DEPLOY_IDENTITY" -g "$RG" -o none 2>/dev/null || az identity create -n "$DEPLOY_IDENTITY" -g "$RG" -l "$LOCATION" -o none
DEPLOY_APP_ID=$(az identity show -n "$DEPLOY_IDENTITY" -g "$RG" --query clientId -o tsv)
DEPLOY_SP=$(az identity show -n "$DEPLOY_IDENTITY" -g "$RG" --query principalId -o tsv)
az identity federated-credential show -n github-master --identity-name "$DEPLOY_IDENTITY" -g "$RG" -o none 2>/dev/null || \
  az identity federated-credential create -n github-master --identity-name "$DEPLOY_IDENTITY" -g "$RG" \
    --issuer https://token.actions.githubusercontent.com \
    --subject "repo:$GITHUB_REPO:ref:refs/heads/master" \
    --audiences api://AzureADTokenExchange -o none
# Newer GitHub repos send the subject with owner and repo IDs (repo:owner@123/name@456:...).
# Set GITHUB_ID_SUBJECT to that form (it appears in the error of a failed deploy) to trust it too.
if [ -n "${GITHUB_ID_SUBJECT:-}" ]; then
  az identity federated-credential show -n github-master-ids --identity-name "$DEPLOY_IDENTITY" -g "$RG" -o none 2>/dev/null || \
    az identity federated-credential create -n github-master-ids --identity-name "$DEPLOY_IDENTITY" -g "$RG" \
      --issuer https://token.actions.githubusercontent.com --subject "$GITHUB_ID_SUBJECT" \
      --audiences api://AzureADTokenExchange -o none
fi
RG_ID=$(az group show -n "$RG" --query id -o tsv)
assign Contributor "$RG_ID" "$DEPLOY_SP" ServicePrincipal
assign AcrPush "$ACR_ID" "$DEPLOY_SP" ServicePrincipal

cat <<DONE

$(printf '\033[1;32m')All set.$(printf '\033[0m')

Add these in GitHub: $GITHUB_REPO > Settings > Secrets and variables > Actions > New repository secret
(they are identifiers, not passwords):

  AZURE_CLIENT_ID        $DEPLOY_APP_ID
  AZURE_TENANT_ID        $TENANT_ID
  AZURE_SUBSCRIPTION_ID  $SUBSCRIPTION_ID

Then run the "Deploy to Azure" workflow (Actions tab), or merge to master.

Your app will be at:  $APP_URL
(It shows a placeholder page until the first deploy finishes.)
DONE
