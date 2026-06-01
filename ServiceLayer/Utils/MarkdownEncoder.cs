using Markdig;
using ReverseMarkdown;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.Text;
using System.Linq;

namespace ServiceLayer.Utils
{
    public static class MarkdownEncoder
    {
        public static string EncodeToMarkdown(this string inputText)
        {
            if (string.IsNullOrEmpty(inputText)) return inputText;

            // List of special characters for MarkdownV2 that must be escaped
            // https://core.telegram.org/bots/api#markdownv2-style
            string[] markdownSpecialChars = { "_", "*", "[", "]", "(", ")", "~", "`", ">", "#", "+", "-", "=", "|", "{", "}", ".", "!" };

            // Escape special Markdown characters
            foreach (string specialChar in markdownSpecialChars)
            {
                inputText = inputText.Replace(specialChar, "\\" + specialChar);
            }

            return inputText;
        }

        public static string ConvertMarkdownToHtml(this string markdownText)
        {
            if (string.IsNullOrWhiteSpace(markdownText)) return markdownText;

            // 1. Convert Markdown to standard HTML using Markdig
            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
            string rawHtml = Markdown.ToHtml(markdownText, pipeline);

            // 2. Sanitize and adapt HTML for Telegram
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);

            var sb = new StringBuilder();
            ProcessNodes(doc.DocumentNode, sb);

            // Clean up multiple newlines
            var result = sb.ToString().Trim();
            return Regex.Replace(result, @"\n{3,}", "\n\n");
        }

        private static string TelegramHtmlEscape(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Replace("&", "&amp;")
                       .Replace("<", "&lt;")
                       .Replace(">", "&gt;");
        }

        private static string ReplaceQuotesWithGuillemets(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    bool isOpening = (i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] == '(' || text[i - 1] == '[' || text[i - 1] == '{');
                    if (isOpening)
                    {
                        sb.Append('«');
                    }
                    else
                    {
                        sb.Append('»');
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static void ProcessNodes(HtmlNode node, StringBuilder sb)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Text)
                {
                    var text = System.Net.WebUtility.HtmlDecode(child.InnerText);
                    bool isInCode = child.AncestorsAndSelf().Any(a => a.Name.ToLower() == "code" || a.Name.ToLower() == "pre");
                    if (!isInCode)
                    {
                        text = ReplaceQuotesWithGuillemets(text);
                    }
                    sb.Append(TelegramHtmlEscape(text));
                    continue;
                }

                if (child.NodeType == HtmlNodeType.Element)
                {
                    string tag = child.Name.ToLower();

                    switch (tag)
                    {
                        case "p":
                        case "div":
                            ProcessNodes(child, sb);
                            sb.AppendLine();
                            break;
                        case "b":
                        case "strong":
                            sb.Append("<b>");
                            ProcessNodes(child, sb);
                            sb.Append("</b>");
                            break;
                        case "i":
                        case "em":
                            sb.Append("<i>");
                            ProcessNodes(child, sb);
                            sb.Append("</i>");
                            break;
                        case "u":
                        case "ins":
                            sb.Append("<u>");
                            ProcessNodes(child, sb);
                            sb.Append("</u>");
                            break;
                        case "s":
                        case "strike":
                        case "del":
                            sb.Append("<s>");
                            ProcessNodes(child, sb);
                            sb.Append("</s>");
                            break;
                        case "code":
                            // If parent is pre, it's a code block, handled by pre case
                            if (child.ParentNode?.Name.ToLower() != "pre")
                            {
                                sb.Append("<code>");
                                ProcessNodes(child, sb);
                                sb.Append("</code>");
                            }
                            else
                            {
                                // Try to get language class (e.g. language-csharp)
                                var classAttr = child.GetAttributeValue("class", "");
                                if (!string.IsNullOrEmpty(classAttr) && classAttr.StartsWith("language-"))
                                {
                                    sb.Append($"<code class=\"{TelegramHtmlEscape(classAttr)}\">");
                                    ProcessNodes(child, sb);
                                    sb.Append("</code>");
                                }
                                else
                                {
                                    ProcessNodes(child, sb);
                                }
                            }
                            break;
                        case "pre":
                            sb.Append("<pre>");
                            ProcessNodes(child, sb);
                            sb.Append("</pre>");
                            break;
                        case "a":
                            var href = child.GetAttributeValue("href", "");
                            if (!string.IsNullOrEmpty(href))
                            {
                                sb.Append($"<a href=\"{TelegramHtmlEscape(href)}\">");
                                ProcessNodes(child, sb);
                                sb.Append("</a>");
                            }
                            else
                            {
                                ProcessNodes(child, sb);
                            }
                            break;
                        case "blockquote":
                            sb.Append("<blockquote>");
                            ProcessNodes(child, sb);
                            sb.Append("</blockquote>");
                            break;
                        case "h1":
                        case "h2":
                        case "h3":
                        case "h4":
                        case "h5":
                        case "h6":
                            sb.Append("<b>");
                            ProcessNodes(child, sb);
                            sb.Append("</b>\n");
                            break;
                        case "ul":
                        case "ol":
                            ProcessNodes(child, sb);
                            sb.AppendLine();
                            break;
                        case "li":
                            sb.Append("• ");
                            ProcessNodes(child, sb);
                            sb.AppendLine();
                            break;
                        case "br":
                            sb.AppendLine();
                            break;
                        default:
                            ProcessNodes(child, sb);
                            break;
                    }
                }
            }
        }

        public static string ConvertHtmlToMarkdown(this string html)
        {
            var converter = new Converter();
            string markdown = converter.Convert(html);
            return markdown;
        }

        public static string WrapWithSpoiler(this string text, global::Telegram.Bot.Types.Enums.ParseMode mode, bool encode = true)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            if (mode == global::Telegram.Bot.Types.Enums.ParseMode.Html)
            {
                var escaped = encode ? TelegramHtmlEscape(text) : text;
                return $"<tg-spoiler><i>{escaped}</i></tg-spoiler>";
            }
            else
            {
                var escaped = encode ? text.EncodeToMarkdown() : text;
                return $"||_{escaped}_||";
            }
        }
    }
}
