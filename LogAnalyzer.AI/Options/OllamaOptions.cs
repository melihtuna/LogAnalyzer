namespace LogAnalyzer.AI.Options;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string Model { get; set; } = "llama3";

    public string Endpoint { get; set; } = "http://localhost:11434/api/generate";
}
