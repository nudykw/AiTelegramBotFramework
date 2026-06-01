global using TgMessage = Telegram.Bot.Types.Message;
global using TgUser = Telegram.Bot.Types.User;
global using AiMessage = OpenAI.Chat.Message;
global using TgKeyboardRow = System.Collections.Generic.List<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton>;
global using TgKeyboardGrid = System.Collections.Generic.List<System.Collections.Generic.List<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton>>;
global using TgUserRepo = DataBaseLayer.Repositories.IRepository<DataBaseLayer.Models.TelegramUserInfo>;
global using TgChatRepo = DataBaseLayer.Repositories.IRepository<DataBaseLayer.Models.TelegramChatInfo>;
global using BillingRepo = DataBaseLayer.Repositories.IRepository<DataBaseLayer.Models.AIBilingItem>;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ServiceLayer.UnitTests")]
[assembly: InternalsVisibleTo("ServiceLayer.IntegrationTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2, PublicKey=0024000004800000940000000602000000240000525341310004000001000100c547cac37abd99c8db225ef2f6c8a3602f3b3606cc9891605d02baa56104f4cfc0734aa39b93bf7852f7d9266654753cc297e7d2edfe0bac1cdcf9f717241550e0a7b191195b7667bb4f64bcb8e2121380fd1d9d46ad2d92d2d15605093924cceaf74c4861eff62abf69b9291ed0a340e113be11e6a7d3113e92484cf7045cc7")]
