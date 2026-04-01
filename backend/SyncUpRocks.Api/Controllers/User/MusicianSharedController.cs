using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SyncUpRocks.Api.Controllers.User;

/// <summary>
/// Note, we have the owner user's id in the route - quite, when songs/playlists/etc shared, that a different authroized user, is acessing a different users \
/// playlist. This controller only deals with readonly/shared access. No updating of main user's assets
/// </summary>
[Authorize]
[ApiController]
[Route("api/musician/shared/{userid:guid}")]
public class MusicianSharedController : ControllerBase
{
    [FromRoute]
    public Guid Userid { get; init; }

    [HttpGet]
    public ActionResult<ApiResponseBase<string>> Get()
    {
        return new ApiResponseBase<string>(true, $"APIs for Public UserProfile - {Userid}" );
    }
}
