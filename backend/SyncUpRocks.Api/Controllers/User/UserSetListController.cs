using Microsoft.AspNetCore.Mvc;

namespace SyncUpRocks.Api.Controllers.User;

[ApiController]
[Route("api/user/setlist/{userid:guid}")]
public class UserSetListController : ControllerBase
{
    [FromRoute]
    public Guid Userid { get; init; }

    [HttpGet]
    public ActionResult<ApiResponseBase<string>> Get()
    {
        return new ApiResponseBase<string>(true, $"APIs for UserProfile - {Userid} TODO: return setlist" );
    }
}
