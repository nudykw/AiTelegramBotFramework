#!/bin/bash

# Configuration
PROJECT="DataBaseLayer/"
STARTUP_PROJECT="TelegramBotApp/"

function usage {
    echo "Usage: ./migr.sh [add|remove|update|list] [MigrationName]"
    echo "Examples:"
    echo "  ./migr.sh add MyNewMigration"
    echo "  ./migr.sh remove"
    echo "  ./migr.sh update"
    echo "  ./migr.sh list"
    exit 1
}

if [ "$1" == "add" ]; then
    if [ -z "$2" ]; then
        echo "Error: Migration name is required for 'add'"
        usage
    fi
    NAME=$2
    echo "=== Adding migration '$NAME' for PostgreSQL ==="
    dotnet ef migrations add "$NAME" \
        --project "$PROJECT" \
        --startup-project "$STARTUP_PROJECT"
    if [ $? -ne 0 ]; then
        echo "Error adding migration."
        exit 1
    fi

elif [ "$1" == "remove" ]; then
    echo "=== Removing last migration ==="
    dotnet ef migrations remove \
        --project "$PROJECT" \
        --startup-project "$STARTUP_PROJECT"

elif [ "$1" == "update" ]; then
    echo "=== Updating database ==="
    dotnet ef database update --project "$PROJECT" --startup-project "$STARTUP_PROJECT"

elif [ "$1" == "list" ]; then
    echo "=== Migrations ==="
    dotnet ef migrations list \
        --project "$PROJECT" \
        --startup-project "$STARTUP_PROJECT"

else
    usage
fi
