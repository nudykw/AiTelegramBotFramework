namespace ServiceLayer.Constans
{
    internal static class MessageMarkers
    {
        internal static string VoiceMessage = "@Voice:";
        internal static string VoiceMessageToBot = $"{VoiceMessage}ToBot:";
        
        internal static class Reactions
        {
            internal const string Like = "👍";
            internal const string Dislike = "👎";
            internal const string Laugh = "😂";
            internal const string Sad = "😢";
            internal const string Think = "🤔";
            internal const string Vomit = "🤮";
        }
    }
}
