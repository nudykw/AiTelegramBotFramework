# Disaster Recovery & Manual Restoration Guide

This guide explains how the automated backup and rollback system works, where backups are stored, and provides step-by-step instructions for a human operator to manually restore the production environment in case of a catastrophic failure.

---

## 1. Automated System Overview

During each deployment (via `.forgejo/workflows/deploy.yml`), the following pipeline runs on the production host:
1. **Pre-flight Disk Check**: Ensures at least 2GB of free space is available on `/home/nudyk`. If not, the deployment aborts *before* stopping the application.
2. **Backup Rotation**: Automatically retains only the **3 most recent backups** under `/home/nudyk/deploy_backups/` to avoid disk exhaustion.
3. **Cold Backup**:
   - Stops the running services (`docker compose down`).
   - Copies `.env` and `Configs/appsettings.json` into the backup directory.
   - Archives all critical Docker named volumes (`postgres_data`, `qdrant_data`, `cloudbeaver_workspace`) into a compressed archive `volumes.tar.gz`.
4. **Deploy & Healthcheck**:
   - Pulls the latest `prod` code, merges secret variables, generates `git_info.json`, builds new Docker images, and starts containers.
   - Monitors the bot container for 60 seconds to ensure it successfully transitions to a `healthy` state.
5. **Atomic Rollback**: If any of the build, migration, or healthcheck steps fail, the pipeline catches the error and automatically restores the environment to the exact pre-deployment state.

---

## 2. Backup File Structure

All backups are stored on the production server under:
```bash
/home/nudyk/deploy_backups/backup_YYYYMMDD_HHMMSS/
```

Inside each backup folder, you will find:
*   `volumes.tar.gz` — Tarball containing the exact archived data of all system Docker volumes.
*   `.env.bak` — Backup of the environment configuration file.
*   `appsettings.json.bak` — Backup of the main application JSON settings.

---

## 3. Manual Restoration Walkthrough

If a deployment fails, the automated rollback *should* recover the system. However, if the rollback process is interrupted or fails due to deep server-level issues, a human operator can execute the manual restoration steps below.

### Step 1: Access the Server
SSH into the production server using your deployment credentials:
```bash
ssh nudyk@<your-deploy-host>
```

### Step 2: Navigate to Project Directory
Navigate to the project root:
```bash
cd /home/nudyk/Sites/GptChatTelegramBot
```

### Step 3: Identify the Target Backup
List all available backups and identify the last known stable backup folder:
```bash
ls -la /home/nudyk/deploy_backups/
```
For example, let's assume the target stable backup is at `/home/nudyk/deploy_backups/backup_20260530_093000`.

### Step 4: Stop All Services
Ensure all currently running containers (including faulty ones) are completely stopped:
```bash
docker compose --profile postgres --profile cloudbeaver --profile aspire down
```

### Step 5: Restore Configuration Files
Restore the previous environment and settings files:
```bash
cp /home/nudyk/deploy_backups/backup_20260530_093000/.env.bak .env
cp /home/nudyk/deploy_backups/backup_20260530_093000/appsettings.json.bak Configs/appsettings.json
```

### Step 6: Restore Docker Volumes
Restore all data volumes from the backup tarball. We use an Alpine helper container to extract the archive cleanly back into the named Docker volumes:
```bash
docker run --rm \
  -v postgres_data:/volumes/postgres_data \
  -v qdrant_data:/volumes/qdrant_data \
  -v cloudbeaver_workspace:/volumes/cloudbeaver_workspace \
  -v /home/nudyk/deploy_backups/backup_20260530_093000:/backup \
  alpine sh -c "rm -rf /volumes/* && tar -xzf /backup/volumes.tar.gz -C /volumes"
```

### Step 7: Revert Code to the Previous Commit
If the failure was caused by buggy code, find the previous stable commit hash from the Git logs or the backup directory name, and hard reset the repository:
```bash
# Check git log to locate the last stable commit on prod
git log --oneline -n 10

# Hard reset the repository to the stable commit (e.g. a1b2c3d4)
git checkout <stable-commit-hash>
git reset --hard <stable-commit-hash>
```

### Step 8: Start the Containers
Rebuild (if code was reverted) and start the containers in the background:
```bash
docker compose --profile postgres --profile cloudbeaver --profile aspire up -d --build
```

### Step 9: Verify System Health
Check container statuses and verify that all services are fully functional:
```bash
# Show container status
docker compose --profile postgres --profile cloudbeaver --profile aspire ps

# Inspect logs to verify bot initialization
docker compose logs -f bot
```
Once the bot and database report healthy, the system is fully recovered.
