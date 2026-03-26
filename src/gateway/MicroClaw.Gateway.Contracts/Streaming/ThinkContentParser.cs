namespace MicroClaw.Gateway.Contracts.Streaming;

/// <summary>从文本中提取 &lt;think&gt; 块，分离思考内容与主文本。</summary>
public static class ThinkContentParser
{
    public static (string Think, string Main) Extract(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (string.Empty, raw ?? string.Empty);

        int start = raw.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        int end = raw.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);

        if (start >= 0 && end > start)
        {
            string think = raw[(start + 7)..end].Trim();
            string main = (raw[..start] + raw[(end + 8)..]).Trim();
            return (think, main);
        }

        return (string.Empty, raw);
    }
}
