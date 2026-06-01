# Configuration
$Project        = "DataBaseLayer/"
$StartupProject = "TelegramBotApp/"

function Show-Usage {
    Write-Host "Usage: ./migr.ps1 [add|remove|update|list] [MigrationName]"
    Write-Host "Examples:"
    Write-Host "  ./migr.ps1 add MyNewMigration"
    Write-Host "  ./migr.ps1 remove"
    Write-Host "  ./migr.ps1 update"
    Write-Host "  ./migr.ps1 list"
}

if ($args.Count -lt 1) {
    Show-Usage
    exit 1
}

$Action = $args[0]

if ($Action -eq "add") {
    if ($args.Count -lt 2) {
        Write-Host "Error: Migration name is required for 'add'" -ForegroundColor Red
        Show-Usage
        exit 1
    }
    $Name = $args[1]
    Write-Host "=== Adding migration '$Name' for PostgreSQL ===" -ForegroundColor Cyan
    dotnet ef migrations add "$Name" `
        --project "$Project" `
        --startup-project "$StartupProject"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error adding migration." -ForegroundColor Red
        exit 1
    }

} elseif ($Action -eq "remove") {
    Write-Host "=== Removing last migration ===" -ForegroundColor Cyan
    dotnet ef migrations remove `
        --project "$Project" `
        --startup-project "$StartupProject"

} elseif ($Action -eq "update") {
    Write-Host "=== Updating database ===" -ForegroundColor Cyan
    dotnet ef database update --project "$Project" --startup-project "$StartupProject"

} elseif ($Action -eq "list") {
    Write-Host "=== Migrations ===" -ForegroundColor Cyan
    dotnet ef migrations list `
        --project "$Project" `
        --startup-project "$StartupProject"

} else {
    Write-Host "Unknown action: $Action" -ForegroundColor Red
    Show-Usage
    exit 1
}
