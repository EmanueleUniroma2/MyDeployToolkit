#!/bin/bash
set -e

# Ensure required variables are set
if [ -z "$BINARY_NAME" ] || [ -z "$DOTNET_VERSION" ]; then
    echo "❌ Error: BINARY_NAME or DOTNET_VERSION not provided."
    exit 1
fi

unset version

# Path Definitions
BASE_DIR="$HOME/ManuLab"
PROJECT_DIR="$BASE_DIR/$BINARY_NAME"
RUNTIME_DIR="$PROJECT_DIR/runtime"
START_SCRIPT="$PROJECT_DIR/start.sh"

echo "🚀 Starting deployment for $BINARY_NAME (Target: $DOTNET_VERSION)..."

# 1. Directory Structure
mkdir -p "$RUNTIME_DIR"

# 2. Update Source
git pull --ff

# 3. Build and Publish
dotnet publish -c Release

# 4. Generate/Update start.sh
cat <<EOF > "$START_SCRIPT"
#!/bin/bash
cd "$RUNTIME_DIR"
./$BINARY_NAME
EOF
chmod +x "$START_SCRIPT"

# 5. Prepare Runtime Folder
find "$RUNTIME_DIR" -mindepth 1 -delete
cp -r "bin/Release/${DOTNET_VERSION}/publish/." "$RUNTIME_DIR/"

# 6. Permissions
chmod +x "$RUNTIME_DIR/$BINARY_NAME"

# 7. Git Sync
git add .
git commit -m "Deployment sync: $(date +'%Y-%m-%d %H:%M:%S')" || echo "No changes to commit."
git push

# 8. PM2 Management
if pm2 describe "$BINARY_NAME" > /dev/null 2>&1; then
    echo "Process '$BINARY_NAME' found. Restarting..."
    pm2 restart "$BINARY_NAME"
else
    echo "Process '$BINARY_NAME' not found. Creating new PM2 process..."
    pm2 start "$START_SCRIPT" --name "$BINARY_NAME"
    pm2 save
fi

echo "✅ Deployment of $BINARY_NAME completed!"