#!/bin/bash

# 1. Clean up binaries and dependencies (Root + 1st Level Only)
# This prevents the AI from analyzing compiled code and saves massive context.
echo "🧹 Cleaning bin, obj, and node_modules folders..."
find . -type d \( -name "bin" -o -name "obj" -o -name "node_modules" \) -prune -print0 | xargs -0 rm -rf

# Run the tool
readmeai --repository "." \
         --api gemini \
         --model gemini-3-pro \
         --output "README.md"

# 2. Git Operations
echo "📦 Committing and Syncing..."
git add .
git commit -m "docs: auto-generate README via AI"
git pull --ff
git push

echo "✅ Done!"
