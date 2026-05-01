using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using LogAnalyzer.Domain.Providers;
using LogAnalyzer.Processor.Queue;
using Microsoft.AspNetCore.Mvc;

namespace LogAnalyzer.Api.Controllers;

[ApiController]
[Route("api/log")]
public class LogController(ILogAnalysisQueue logAnalysisQueue) : ControllerBase
{
    [HttpPost("analyze")]
    public async Task<ActionResult<LogAnalysisResponse>> Analyze([FromBody] LogRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Logs))
        {
            return BadRequest("Logs payload is required.");
        }

        try
        {
            var result = await logAnalysisQueue.EnqueueAsync(
                new StaticLogProvider(request.Logs),
                request.IncludeRawAIResponse,
                cancellationToken);
            return Ok(result);
        }
        catch (QueueFullException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, ex.Message);
        }
    }
}
