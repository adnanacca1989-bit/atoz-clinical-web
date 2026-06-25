#!/usr/bin/env bash
# Creates a GitHub Project board from .github/roadmap/items.json
#
# Prerequisites:
#   gh auth login
#   export GITHUB_REPO=owner/AtoZClinicalWeb   # or pass as first argument
#
# Usage:
#   ./scripts/setup-github-roadmap-project.sh [owner/repo] [--dry-run]

set -euo pipefail

DRY_RUN=false
REPO="${GITHUB_REPO:-}"

for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=true ;;
    *) REPO="$arg" ;;
  esac
done

if [[ -z "$REPO" ]]; then
  if command -v git >/dev/null 2>&1 && git remote get-url origin >/dev/null 2>&1; then
    REPO="$(git remote get-url origin | sed -E 's#.*github.com[:/]([^/]+)/([^/.]+)(\.git)?$#\1/\2#')"
  fi
fi

if [[ -z "$REPO" ]]; then
  echo "Set GITHUB_REPO or pass owner/repo as the first argument." >&2
  exit 1
fi

REPO_OWNER="${REPO%%/*}"
REPO_NAME="${REPO##*/}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ITEMS_FILE="$SCRIPT_DIR/../.github/roadmap/items.json"

if [[ ! -f "$ITEMS_FILE" ]]; then
  echo "Missing $ITEMS_FILE" >&2
  exit 1
fi

if ! command -v gh >/dev/null 2>&1; then
  echo "Install GitHub CLI: https://cli.github.com/" >&2
  exit 1
fi

if [[ "$DRY_RUN" != true ]]; then
  gh auth status >/dev/null
fi

PROJECT_TITLE="$(jq -r '.projectTitle' "$ITEMS_FILE")"
PROJECT_DESC="$(jq -r '.projectDescription' "$ITEMS_FILE")"

run() {
  if [[ "$DRY_RUN" == true ]]; then
    echo "[dry-run] $*"
  else
  "$@"
  fi
}

label_color() {
  case "$1" in
    critical) echo d73a4a ;;
    important) echo fbca04 ;;
    future) echo 0e8a16 ;;
    security) echo b60205 ;;
    compliance) echo 5319e7 ;;
    *) echo ededed ;;
  esac
}

echo "Roadmap setup for $REPO"

while IFS= read -r label; do
  color="$(label_color "$label")"
  run gh label create "$label" --repo "$REPO" --color "$color" --force >/dev/null || true
done < <(jq -r '.labels[]' "$ITEMS_FILE")

declare -A MILESTONE_NUM
while IFS=$'\t' read -r key title desc; do
  if [[ "$DRY_RUN" == true ]]; then
    MILESTONE_NUM["$key"]=0
    continue
  fi
  existing="$(gh api "repos/$REPO/milestones" --jq ".[] | select(.title==\"$title\") | .number" | head -n1)"
  if [[ -n "$existing" ]]; then
    MILESTONE_NUM["$key"]="$existing"
  else
    num="$(gh api "repos/$REPO/milestones" -f title="$title" -f description="$desc" --jq .number)"
    MILESTONE_NUM["$key"]="$num"
  fi
  echo "Milestone: $title (#${MILESTONE_NUM[$key]})"
done < <(jq -r '.milestones[] | [.key, .title, .description] | @tsv' "$ITEMS_FILE")

if [[ "$DRY_RUN" == true ]]; then
  PROJECT_NUMBER=1
  PROJECT_ID=DRY_RUN
else
  PROJECT_JSON="$(gh project create --owner "$REPO_OWNER" --title "$PROJECT_TITLE" --format json)"
  PROJECT_NUMBER="$(echo "$PROJECT_JSON" | jq -r .number)"
  PROJECT_ID="$(echo "$PROJECT_JSON" | jq -r .id)"
  echo "Created project #$PROJECT_NUMBER"
fi

create_field() {
  local name="$1"
  local options="$2"
  if [[ "$DRY_RUN" == true ]]; then
    echo "[dry-run] field $name: $options"
    return
  fi
  gh project field-create "$PROJECT_NUMBER" --owner "$REPO_OWNER" \
    --name "$name" --data-type SINGLE_SELECT \
    --single-select-options "$options" --format json >/dev/null
}

create_field "Priority" "Critical,Important,Future"
create_field "Status" "Todo,In Progress,Done"
create_field "Effort" "XS,S,M,L,XL,Ongoing"

field_option_id() {
  local field_name="$1"
  local option_name="$2"
  gh project field-list "$PROJECT_NUMBER" --owner "$REPO_OWNER" --format json \
    | jq -r --arg fn "$field_name" --arg on "$option_name" \
      '.fields[] | select(.name==$fn) | .options[]? | select(.name==$on) | .id' | head -n1
}

map_effort() {
  local effort="$1"
  case "$effort" in
    *day*) echo XS ;;
    1*) echo S ;;
    2*) echo M ;;
    4*|5*|6*|8*) echo L ;;
    *12*|*Very*) echo XL ;;
    *Ongoing*) echo Ongoing ;;
    *) echo M ;;
  esac
}

PRIORITY_FIELD="$(field_option_id Priority Critical | sed 's/-.*//')" || true
# field id is parent; get field ids properly
get_field_id() {
  gh project field-list "$PROJECT_NUMBER" --owner "$REPO_OWNER" --format json \
    | jq -r --arg fn "$1" '.fields[] | select(.name==$fn) | .id' | head -n1
}

PRIORITY_FIELD_ID="$(get_field_id Priority)"
STATUS_FIELD_ID="$(get_field_id Status)"
EFFORT_FIELD_ID="$(get_field_id Effort)"

ISSUE_COUNT=0
while IFS= read -r item; do
  id="$(echo "$item" | jq -r .id)"
  title="$(echo "$item" | jq -r .title)"
  priority="$(echo "$item" | jq -r .priority)"
  milestone_key="$(echo "$item" | jq -r .milestone)"
  business="$(echo "$item" | jq -r .businessValue)"
  complexity="$(echo "$item" | jq -r .complexity)"
  effort="$(echo "$item" | jq -r .effort)"
  labels="$(echo "$item" | jq -r '.labels | join(",")')"

  body="## Roadmap item ${id}

| Field | Value |
|-------|-------|
| **Priority** | ${priority} |
| **Business value** | ${business} |
| **Complexity** | ${complexity} |
| **Effort** | ${effort} |

---
_Auto-generated from \`.github/roadmap/items.json\`._"

  if [[ "$DRY_RUN" == true ]]; then
    echo "[dry-run] issue: [$id] $title"
    continue
  fi

  ms_num="${MILESTONE_NUM[$milestone_key]}"
  issue_url="$(gh issue create --repo "$REPO" \
    --title "[$id] $title" \
    --body "$body" \
    --label "$labels" \
    --milestone "$ms_num")"

  item_json="$(gh project item-add "$PROJECT_NUMBER" --owner "$REPO_OWNER" --url "$issue_url" --format json)"
  project_item_id="$(echo "$item_json" | jq -r .id)"

  pri_opt="$(field_option_id Priority "$priority")"
  status_opt="$(field_option_id Status Todo)"
  effort_opt="$(field_option_id Effort "$(map_effort "$effort")")"

  for pair in "$PRIORITY_FIELD_ID:$pri_opt" "$STATUS_FIELD_ID:$status_opt" "$EFFORT_FIELD_ID:$effort_opt"; do
    fid="${pair%%:*}"
    oid="${pair##*:}"
    [[ -z "$oid" || "$oid" == "$fid" ]] && continue
    gh project item-edit --id "$project_item_id" --project-id "$PROJECT_ID" \
      --field-id "$fid" --single-select-option-id "$oid" >/dev/null
  done

  echo "Issue: $issue_url"
  ISSUE_COUNT=$((ISSUE_COUNT + 1))
done < <(jq -c '.items[]' "$ITEMS_FILE")

echo ""
echo "Created $ISSUE_COUNT issues."
echo "Open board: gh project view $PROJECT_NUMBER --owner $REPO_OWNER --web"
echo "Tip: In the project, add a Board view grouped by Priority."
