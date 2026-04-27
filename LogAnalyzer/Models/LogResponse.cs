namespace LogAnalyzer.Models
{
    public class LogResponse
    {
        public string Summary { get; set; } = string.Empty;
        public string RootCause { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Suggestion { get; set; } = string.Empty;
    }
}
