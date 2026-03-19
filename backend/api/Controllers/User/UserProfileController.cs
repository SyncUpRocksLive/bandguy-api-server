using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.User;

[ApiController]
[Route("api/user/profile/{userid:guid}")]
public class UserProfileControler : ControllerBase
{
    [FromRoute]
    public Guid Userid { get; init; }

    [HttpGet]
    public ActionResult<ApiResponseBase<string>> Get()
    {
        // NOTE: On first account login/setup, we may need to init basic profile data...
        return new ApiResponseBase<string>(true,
            $"APIs for UserProfile - {Userid} TODO: Return non-secret profile data for logged in user" 
        );
    }
}
