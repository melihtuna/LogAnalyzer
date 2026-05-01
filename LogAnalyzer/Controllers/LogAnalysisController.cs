using LogAnalyzer.Domain.Interfaces;
using LogAnalyzer.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace LogAnalyzer.Api.Controllers;

[ApiController]
[Route("log-analysis")]
public class LogAnalysisController(ILogAnalysisRunRepository logAnalysisRunRepository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LogAnalysis>>> Get(CancellationToken cancellationToken)
    {
        var analyses = await logAnalysisRunRepository.GetLatestAsync(cancellationToken);
        return Ok(analyses);
    }
}

