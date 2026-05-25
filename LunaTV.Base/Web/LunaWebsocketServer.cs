using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace LunaTV.Base.Web;

public class LunaWebsocketServer
{
    private readonly HttpService service;

    public LunaWebsocketServer()
    {
        service = new HttpService();
    }

    public List<IHttpSession> ClinetList { get; set; } = new();

    public async Task Start()
    {
        await service.SetupAsync(new TouchSocketConfig() //加载配置
            .SetListenIPHosts(4040)
            .ConfigureContainer(a => { a.AddConsoleLogger(); })
            .ConfigurePlugins(a =>
            {
                a.UseWebSocket(options =>
                {
                    options.SetUrl("/ws");
                    options.SetAutoPong(true);
                });

                a.Add(typeof(IWebSocketConnectedPlugin), async (IWebSocket client, HttpContextEventArgs e) =>
                {
                    ClinetList.Add(client.Client);
                    await e.InvokeNext();
                });
                a.Add(typeof(IWebSocketClosingPlugin), async (IWebSocket client, ClosedEventArgs e) =>
                {
                    ClinetList.Remove(client.Client);
                    await e.InvokeNext();
                });

                a.Add(typeof(IWebSocketReceivedPlugin), async (IWebSocket client, WSDataFrameEventArgs e) =>
                {
                    switch (e.DataFrame.Opcode)
                    {
                        case WSDataType.Close:
                        {
                            await client.CloseAsync("断开");
                        }
                            return;
                        case WSDataType.Ping:
                            await client.PongAsync(); //收到ping时，一般需要响应pong
                            break;
                        case WSDataType.Pong:
                            break;
                    }

                    await e.InvokeNext();
                });
            }));
        await service.StartAsync();
    }


    public void SendDatas()
    {
        for (var i = 0; i < 200; i++)
            Task.Run(async () =>
            {
                while (true)
                    try
                    {
                        var clientList = ClinetList.ToList();
                        for (var j = 0; j < clientList.Count; j++)
                        {
                            var sock = (HttpSessionClient)clientList[j];
                            if (sock.Online)
                                await sock.WebSocket.SendAsync(
                                    $"Dev[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff")}, 12.34, 34.56, 56.78, \"77705683\"]");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("出现异常：" + ex.Message + "\r\n" + ex.StackTrace);
                    }
                    finally
                    {
                        await Task.Delay(10);
                    }
            });
    }
}