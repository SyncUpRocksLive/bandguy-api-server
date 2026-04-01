using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;


namespace SyncUpRocks.Api.Controllers.User;

[Authorize]
[ApiController]
[Route("api/musician")]
public class MusicianController : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponseBase<string>> Get()
    {
        return new ApiResponseBase<string>(true, $"APIs for UserProfile private");
    }
}

// Need to up ulimit for tcp/sockets. ~10000?
// Only the main bandleader should keep ws open. As, all other peers are connecting to the leader

//[ApiController]
//[Route("ws")]
//public class PeeringController : ControllerBase
//{
//    private readonly ILogger<PeeringController> _logger;

//    public PeeringController(ILogger<PeeringController> logger)
//    {
//        _logger = logger;
//    }

//    [HttpGet("session/{id:long}")]
//    public async Task Get(long id, CancellationToken ct)
//    {
//        if (HttpContext.WebSockets.IsWebSocketRequest)
//        {
//            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
//            _logger.LogInformation("WebSocket connection established for Musician {Id}", id);

//            await HandlePeeringSession(id, webSocket, ct);
//        }
//        else
//        {
//            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
//        }
//    }

//    private async Task HandlePeeringSession(long id, WebSocket webSocket, CancellationToken ct)
//    {
//        var buffer = new byte[1024 * 4];

//        // Loop as long as the connection is open
//        while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
//        {
//            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

//            if (result.MessageType == WebSocketMessageType.Close)
//            {
//                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
//            }
//            else
//            {
//                // Echo logic or Peering Logic here
//                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
//                _logger.LogDebug("Received from {Id}: {Message}", id, message);

//                // For a 'Leader', you might broadcast this to other stored sockets
//                var response = Encoding.UTF8.GetBytes($"Ack: {DateTime.UtcNow}");
//                await webSocket.SendAsync(new ArraySegment<byte>(response), WebSocketMessageType.Text, true, ct);
//            }
//        }
//    }
//}

//// Simple Singleton to manage the thousands of band members
//public class PeeringManager
//{
//    // Key: Musician BIGINT ID, Value: The Active Socket
//    private readonly ConcurrentDictionary<long, WebSocket> _sessions = new();

//    public void AddSession(long id, WebSocket socket) => _sessions[id] = socket;

//    public async Task BroadcastToPeers(long leaderId, string message)
//    {
//        // Real-world logic would involve "Rooms" or "Bands"
//        if (_sessions.TryGetValue(leaderId, out var socket) && socket.State == WebSocketState.Open)
//        {
//            var bytes = Encoding.UTF8.GetBytes(message);
//            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
//        }
//    }
//}