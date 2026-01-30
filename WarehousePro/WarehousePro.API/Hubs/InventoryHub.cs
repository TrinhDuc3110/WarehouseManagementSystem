using Microsoft.AspNetCore.SignalR;

namespace WarehousePro.API.Hubs;

public class InventoryHub : Hub
{
    public async Task SendUpdate(string message)
    {
        await Clients.All.SendAsync("ReceiveUpdate", message);
    }
}