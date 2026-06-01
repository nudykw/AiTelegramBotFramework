dotnet clean --configuration Release || exit /b
dotnet publish TelegramBotApp\TelegramBotApp.csproj /p:PublishProfile=TelegramBotApp\Properties\PublishProfiles\FolderProfile.pubxml || exit /b
del C:\Projects\Net\GPTChatTelegramBot\TelegramBotApp\bin\publish\StoreContext.db
scp -rv -i C:\Users\Nudyk\Private-ssh-key-2022-12-13.key C:\Projects\Net\GPTChatTelegramBot\TelegramBotApp\bin\publish\* ubuntu@193.123.56.73:/home/ubuntu/applications/gptNudykTelegramBot || exit /b
ssh -i ~/Private-ssh-key-2022-12-13.key ubuntu@193.123.56.73 "sudo systemctl restart gptNudykBot.service"
ssh -i ~/Private-ssh-key-2022-12-13.key ubuntu@193.123.56.73 "sudo journalctl -u gptNudykBot -f"