#!/bin/bash
# Exit immediately if a command exits with a non-zero status
set -e

# Store the current branch to return to it at the end
ORIGINAL_BRANCH=$(git branch --show-current)
trap 'git checkout "$ORIGINAL_BRANCH" 2>/dev/null || true' EXIT

FROM_BRANCH=""
TO_BRANCH=""
COMMIT_MSG=""

# Help message
show_help() {
  echo "Usage: ./git-sinc.sh -f <from_branch> -t <to_branch> -m <commit_message> [file1] [file2] ..."
  echo ""
  echo "Automates staging, local committing, and merging between branches."
  echo ""
  echo "Options:"
  echo "  -h, --help            Show this help message and exit"
  echo "  -f, --from <branch>   Base branch to commit on (required)"
  echo "  -t, --to <branch>     Target branch to merge into (required)"
  echo "  -m, --message <msg>   Commit message (required)"
  echo ""
  echo "Example:"
  echo "  ./git-sinc.sh -f main -t prod -m \"feat(git): add sinc helper\" git-sinc.sh"
}

# Show help if run without parameters or with help flags
if [ $# -eq 0 ] || [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
  show_help
  exit 0
fi

# Parse options
while [[ "$1" =~ ^- ]]; do
  case "$1" in
    -m|--message)
      COMMIT_MSG="$2"
      shift 2
      ;;
    -f|--from)
      FROM_BRANCH="$2"
      shift 2
      ;;
    -t|--to)
      TO_BRANCH="$2"
      shift 2
      ;;
    -h|--help)
      show_help
      exit 0
      ;;
    *)
      echo "Unknown option: $1"
      echo ""
      show_help
      exit 1
      ;;
  esac
done

if [ -z "$FROM_BRANCH" ] || [ -z "$TO_BRANCH" ]; then
  echo "Error: Both base branch (-f/--from) and target branch (-t/--to) are required."
  echo ""
  show_help
  exit 1
fi

if [ -z "$COMMIT_MSG" ]; then
  echo "Error: Commit message is required via -m or --message option."
  echo ""
  show_help
  exit 1
fi

# The remaining positional arguments are files
FILES=("$@")

# Ensure we are on the base branch
if [ "$(git branch --show-current)" != "$FROM_BRANCH" ]; then
  echo "→ Switching to base branch '$FROM_BRANCH'..."
  git checkout "$FROM_BRANCH"
fi

# Staging files: always prioritize explicit list of files
if [ ${#FILES[@]} -gt 0 ]; then
  echo "→ Staging specified files: ${FILES[@]}"
  git add "${FILES[@]}"
else
  echo "Warning: No files specified. Staging all tracked modifications..."
  git add -u
fi

echo "→ Committing on branch '$FROM_BRANCH'..."
git commit -m "$COMMIT_MSG"

# Switch to target branch and merge
echo "→ Switching to target branch '$TO_BRANCH'..."
git checkout "$TO_BRANCH"

echo "→ Merging '$FROM_BRANCH' into '$TO_BRANCH'..."
git merge "$FROM_BRANCH" --no-edit

echo ""
echo "✓ Done! Committed and merged successfully from '$FROM_BRANCH' to '$TO_BRANCH'."
