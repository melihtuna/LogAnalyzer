namespace LogAnalyzer.Tools
{
    public class LogParser
    {
        public string ExtractErrorLinesOrFullLogs(string logs)
        {
            if (string.IsNullOrWhiteSpace(logs))
            {
                return string.Empty;
            }

            var lines = logs
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (lines.Length == 0)
            {
                return logs;
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
