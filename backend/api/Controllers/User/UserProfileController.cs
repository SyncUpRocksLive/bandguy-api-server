using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.User;

[ApiController]
[Route("api/user/profile")]
public class UserProfileControler : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        // NOTE: On first account login/setup, we may need to init basic profile data...
        return Ok(new { Message = "APIs for UserProfile - TODO: Return non-secret profile data for logged in user" });
    }

    [HttpGet("test")]
    public IActionResult Test([Range(0, 4)]int y)
    {
        // NOTE: On first account login/setup, we may need to init basic profile data...
        return Ok(new { Message = "APIs for UserProfile - TODO: Return non-secret profile data for logged in user" });
    }
}
