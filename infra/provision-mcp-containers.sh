#!/usr/bin/env bash
set -euo pipefail

: "${AZURE_RESOURCE_GROUP:?Set AZURE_RESOURCE_GROUP}"
: "${COSMOS_ACCOUNT:?Set COSMOS_ACCOUNT}"
: "${COSMOS_DATABASE:?Set COSMOS_DATABASE}"

ensure_container() {
  local name="$1" ttl="$2"
  if az cosmosdb sql container show \
      --resource-group "$AZURE_RESOURCE_GROUP" \
      --account-name "$COSMOS_ACCOUNT" \
      --database-name "$COSMOS_DATABASE" \
      --name "$name" >/dev/null 2>&1; then
    az cosmosdb sql container update \
      --resource-group "$AZURE_RESOURCE_GROUP" \
      --account-name "$COSMOS_ACCOUNT" \
      --database-name "$COSMOS_DATABASE" \
      --name "$name" \
      --ttl "$ttl" >/dev/null
    return
  fi
  az cosmosdb sql container create \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --account-name "$COSMOS_ACCOUNT" \
    --database-name "$COSMOS_DATABASE" \
    --name "$name" \
    --partition-key-path /userId \
    --ttl "$ttl" >/dev/null
}

ensure_container McpKeys -1
ensure_container McpUsage 172800
