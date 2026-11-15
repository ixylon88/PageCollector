using System.Net;
using System.Text;

namespace PageCollector.Core;
/// <summary>Reads the saved HTML head without holding embedded resources in memory.</summary>
public static class HtmlTitleReader
{
    public static Task<string> ReadAsync(string filePath, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var input = new Input(reader, cancellationToken);
        var htmlSeen = false;

        while (input.Read() is var character && character != -1)
        {
            if (character != '<')
            {
                continue;
            }

            var tag = ReadTag(input);

            if (tag is "html" or "head" or "!doctype" or "body")
            {
                htmlSeen = true;
            }

            if (tag is "body" or "/head")
            {
                break;
            }

            if (tag == "title")
            {
                return TitleFilter.Normalize(WebUtility.HtmlDecode(ReadRawText(input, "title", keepText: true)));
            }

            if (tag is "script" or "style")
            {
                ReadRawText(input, tag, keepText: false);
            }

            if (tag == "template")
            {
                SkipTemplate(input);
            }
        }

        if (!htmlSeen)
        {
            throw new IOException("Die erzeugte Datei scheint kein HTML-Dokument zu sein.");
        }

        return "";
    }, cancellationToken);

    private static string ReadTag(Input input)
    {
        var name = new StringBuilder();
        var c = input.Read();

        // Comments can contain apparent title tags, including SingleFile source metadata.
        if (c == '!')
        {
            name.Append('!');
            c = input.Read();

            if (c == '-' && input.Peek() == '-')
            {
                input.Read();
                var hyphens = 0;

                while ((c = input.Read()) != -1)
                {
                    if (c == '>' && hyphens >= 2)
                    {
                        break;
                    }

                    hyphens = c == '-' ? hyphens + 1 : 0;
                }

                return "";
            }
        }

        if (c == '/')
        {
            name.Append('/');
            c = input.Read();
        }

        while (c != -1 && (char.IsAsciiLetterOrDigit((char)c) || c is ':' or '-'))
        {
            if (name.Length < 64)
            {
                name.Append(char.ToLowerInvariant((char)c));
            }

            c = input.Read();
        }

        var quote = 0;

        while (c != -1)
        {
            if (quote != 0)
            {
                if (c == quote)
                {
                    quote = 0;
                }
            }
            else if (c is '\'' or '"')
            {
                quote = c;
            }
            else if (c == '>')
            {
                break;
            }

            c = input.Read();
        }

        return name.ToString();
    }

    private static string ReadRawText(Input input, string tag, bool keepText)
    {
        var text = keepText ? new StringBuilder() : null;
        var closing = "</" + tag;

        while (input.Read() is var c && c != -1)
        {
            if (c == '<')
            {
                var prefix = new StringBuilder("<");

                for (var i = 1; i < closing.Length; i++)
                {
                    var next = input.Peek();

                    if (next == -1 || char.ToLowerInvariant((char)next) != closing[i])
                    {
                        break;
                    }

                    prefix.Append((char)input.Read());
                }

                var boundary = input.Peek();

                if (prefix.Length == closing.Length && (boundary == '>' || boundary == '/' || boundary != -1 && char.IsWhiteSpace((char)boundary)))
                {
                    while ((c = input.Read()) != -1 && c != '>')
                    {
                    }

                    return text?.ToString() ?? "";
                }

                text?.Append(prefix);
            }
            else
            {
                text?.Append((char)c);
            }

            // A title of this size is not useful and should not exhaust memory.
            if (text?.Length > 65536)
            {
                throw new IOException("Der HTML-Seitentitel ist ungewöhnlich groß (über 65.536 Zeichen).");
            }
        }

        if (keepText)
        {
            throw new IOException("Der HTML-Seitentitel ist unvollständig.");
        }

        return "";
    }

    private static void SkipTemplate(Input input)
    {
        var depth = 1;

        while (depth > 0 && input.Read() is var c && c != -1)
        {
            if (c != '<')
            {
                continue;
            }

            var tag = ReadTag(input);

            if (tag == "template")
            {
                depth++;
            }
            else if (tag == "/template")
            {
                depth--;
            }
            else if (tag is "script" or "style")
            {
                ReadRawText(input, tag, keepText: false);
            }
        }
    }

    private sealed class Input(StreamReader reader, CancellationToken cancellationToken)
    {
        private int count;

        public int Read()
        {
            if ((count++ & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return reader.Read();
        }

        public int Peek() => reader.Peek();
    }
}