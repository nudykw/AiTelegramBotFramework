using System;

namespace ServiceLayer.Constans;

/// <summary>
/// Bot command scopes and permissions (names match BotCommandScope in the Telegram API).
/// </summary>
[Flags]
public enum BotCommandScope
{
    /// <summary>
    /// Default (all users).
    /// </summary>
    Default = 1,

    /// <summary>
    /// All private chats.
    /// </summary>
    AllPrivateChats = 2,

    /// <summary>
    /// All group chats.
    /// </summary>
    AllGroupChats = 4,

    /// <summary>
    /// All group and supergroup administrators.
    /// </summary>
    AllChatAdmins = 8,

    /// <summary>
    /// Specific chat.
    /// </summary>
    Chat = 16,

    /// <summary>
    /// Administrators of a specific chat.
    /// </summary>
    ChatAdmins = 32,

    /// <summary>
    /// Specific member of a specific chat.
    /// </summary>
    ChatMember = 64,

    /// <summary>
    /// Bot owner (internal extension).
    /// </summary>
    Owner = 128,

    /// <summary>
    /// Any administrator (group admin or owner).
    /// </summary>
    AnyAdmin = AllChatAdmins | Owner
}
