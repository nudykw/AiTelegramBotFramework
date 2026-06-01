using Telegram.Bot.Types.ReplyMarkups;

namespace ServiceLayer.Utils;

public static class TelegramKeyboardHelper
{
    /// <summary>
    /// Creates an inline keyboard for selection with a checkmark for the current value.
    /// </summary>
    /// <typeparam name="T">Item type</typeparam>
    /// <param name="items">List of items</param>
    /// <param name="labelSelector">Function to get button text</param>
    /// <param name="dataSelector">Function to get callback data</param>
    /// <param name="currentValue">Currently selected value (to be marked with ✅)</param>
    /// <param name="columns">Number of columns</param>
    /// <returns>InlineKeyboardMarkup</returns>
    public static InlineKeyboardMarkup CreateSelectionKeyboard<T>(
        IEnumerable<T> items,
        Func<T, string> labelSelector,
        Func<T, string> dataSelector,
        string? currentValue = null,
        int columns = 1)
    {
        var rows = new List<List<InlineKeyboardButton>>();
        var currentRow = new List<InlineKeyboardButton>();
        
        int index = 0;
        foreach (var item in items)
        {
            var data = dataSelector(item);
            var label = labelSelector(item);
            
            // Add checkmark if this is the current value
            if (data == currentValue || (currentValue != null && data.EndsWith($":{currentValue}")))
            {
                label = $"✅ {label}";
            }

            var button = InlineKeyboardButton.WithCallbackData(label, data);
            
            currentRow.Add(button);
            index++;

            if (index % columns == 0)
            {
                rows.Add(currentRow);
                currentRow = new List<InlineKeyboardButton>();
            }
        }

        if (currentRow.Any())
        {
            rows.Add(currentRow);
        }

        return new InlineKeyboardMarkup(rows);
    }

    /// <summary>
    /// Creates a generic inline keyboard.
    /// </summary>
    public static InlineKeyboardMarkup CreateInlineKeyboard<T>(
        IEnumerable<T> items,
        Func<T, string> buttonTextSelector,
        Func<T, string> callbackDataSelector,
        int columns = 1)
    {
        var rows = new List<List<InlineKeyboardButton>>();
        var currentRow = new List<InlineKeyboardButton>();

        int index = 0;
        foreach (var item in items)
        {
            var button = InlineKeyboardButton.WithCallbackData(buttonTextSelector(item), callbackDataSelector(item));
            currentRow.Add(button);
            index++;

            if (index % columns == 0)
            {
                rows.Add(currentRow);
                currentRow = new List<InlineKeyboardButton>();
            }
        }

        if (currentRow.Any())
        {
            rows.Add(currentRow);
        }

        return new InlineKeyboardMarkup(rows);
    }
}
