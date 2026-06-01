using System;

namespace DataBaseLayer.Enums
{
    [Flags]
    public enum MessageReactionType
    {
        None = 0,
        // Group 1: Quality Rating (👍/👎)
        Like = 1 << 0,
        Dislike = 1 << 1,
        // Group 2: Emotional Tone (😂/😢)
        Laugh = 1 << 2,
        Sad = 1 << 3,
        // Group 3: Interest (🤔/🤮)
        Think = 1 << 4,
        Vomit = 1 << 5
    }
}
