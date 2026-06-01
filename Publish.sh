#!/bin/bash

dotnet clean --configuration Release || exit
dotnet publish TelegramBotApp/TelegramBotApp.csproj /p:PublishProfile=TelegramBotApp/Properties/PublishProfiles/FolderProfile.pubxml || exit
rm -f ./gptNudykTelegramBot/StoreContext.db
scp -rv -i ~/Private-ssh-key-2022-12-13.key ./TelegramBotApp/bin/publish/* ubuntu@193.123.56.73:/home/ubuntu/applications/gptNudykTelegramBot || exit
ssh -i ~/Private-ssh-key-2022-12-13.key ubuntu@193.123.56.73 "sudo systemctl restart gptNudykBot.service"
ssh -i ~/Private-ssh-key-2022-12-13.key ubuntu@193.123.56.73 "sudo journalctl -u gptNudykBot -f"
