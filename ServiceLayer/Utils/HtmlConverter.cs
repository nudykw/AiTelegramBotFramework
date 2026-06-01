using HtmlAgilityPack;

namespace ServiceLayer.Utils
{
    public static class HtmlTagFilter
    {
        private static string[] telegramTags = new string[]
            {
                "b", "strong",
                "i", "em",
                "code",
                "s", "strike", "del",
                "u",
                "pre",
                "a",
                "tg-spoiler",
                "blockquote"
            };
        public static string ConvertHtmlToTelegramHtml(this string html)
        {
            var telegramHtml = KeepOnlySelectedTags(html, telegramTags);
            // Replace literal angle brackets which might confuse Telegram if unencoded
            // but we can't just replace all because we have actual html tags.
            // Telegram usually complains if there are < > that don't form valid allowed tags.
            return telegramHtml;
        }
        private static string KeepOnlySelectedTags(string html, params string[] selectedTags)
        {
            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);

            var nodesToRemove = new System.Collections.Generic.List<HtmlNode>();
            foreach (var node in htmlDocument.DocumentNode.Descendants())
            {
                if (node.NodeType == HtmlNodeType.Element && !selectedTags.Contains(node.Name.ToLowerInvariant()))
                {
                    nodesToRemove.Add(node);
                }
            }

            foreach (var node in nodesToRemove)
            {
                var parent = node.ParentNode;
                if (parent != null)
                {
                    foreach (var child in node.ChildNodes.ToArray())
                    {
                        parent.InsertBefore(child, node);
                    }
                    parent.RemoveChild(node);
                }
            }

            return htmlDocument.DocumentNode.InnerHtml;
        }
    }
}
