namespace LogAnalyzer.Controllers
{
    using LogAnalyzer.Models;
    using LogAnalyzer.Services;
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route("api/log")]
    public class LogController : ControllerBase
    {
        private readonly LogAnalysisService _logAnalysisService;

        public LogController(LogAnalysisService logAnalysisService)
        {
            _logAnalysisService = logAnalysisService;
        }

        [HttpPost("analyze")]
        public async Task<ActionResult<LogResponse>> Analyze([FromBody] LogRequest request, CancellationToken cancellationToken)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Logs))
            {
                return BadRequest("Logs payload is required.");
            }

            var result = await _logAnalysisService.AnalyzeAsync(request, cancellationToken);
            return Ok(result);
        }
    }
}
