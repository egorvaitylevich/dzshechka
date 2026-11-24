using System.Net;
using System.Net.Sockets;
using System.Text;

// ============================================================
//  ЗАДАНИЕ №5. РЕАЛИЗАЦИЯ MULTICAST-ПРИЛОЖЕНИЯ
//
//  Multicast-сервер периодически рассылает сообщения в заданную
//  группу. Несколько Multicast-клиентов одновременно подписаны
//  на группу и принимают эти сообщения (групповое оповещение),
//  после чего обмениваются сообщениями в режиме группового чата.
// ============================================================

const string GroupAddressText = "239.0.0.222";   // адрес multicast-группы
const int Port = 65499;                          // порт группы
const int ClientCount = 3;                       // число одновременно работающих клиентов
const int NotificationCount = 5;                 // сколько уведомлений рассылает сервер
const int NotificationIntervalMs = 1000;         // период рассылки

IPAddress group = IPAddress.Parse(GroupAddressText);
IPEndPoint groupEp = new(group, Port);

Console.WriteLine("=========================================================");
Console.WriteLine("     ЗАДАНИЕ №5. РЕАЛИЗАЦИЯ MULTICAST-ПРИЛОЖЕНИЯ");
Console.WriteLine("=========================================================");
Console.WriteLine($"Multicast-группа : {group}:{Port}");
Console.WriteLine($"Клиентов         : {ClientCount}");
Console.WriteLine($"Уведомлений      : {NotificationCount}, период {NotificationIntervalMs} мс");
Console.WriteLine();

using var cts = new CancellationTokenSource();

// --- Клиенты подписываются на группу ---
var clients = new List<MulticastClient>();
for (int i = 1; i <= ClientCount; i++)
{
    var client = new MulticastClient($"КЛИЕНТ-{i}", group, Port);
    client.Join();
    clients.Add(client);
}
Console.WriteLine();

// --- Фаза 1: сервер периодически рассылает уведомления в группу ---
Console.WriteLine("--- Фаза 1. Периодическая рассылка от сервера ---");

// Все клиенты начинают принимать одновременно.
Task[] receiveTasks = clients.Select(c => c.ReceiveLoopAsync(cts.Token)).ToArray();

using (var server = new UdpClient())
{
    // MulticastLoopback = true -> датаграмма доставляется и на этот же узел,
    // поэтому клиенты, запущенные на этой машине, тоже её получают.
    server.MulticastLoopback = true;
    server.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
    ConsoleLog.Write("СЕРВЕР", $"запущен, рассылает в группу {group}:{Port}");

    for (int i = 1; i <= NotificationCount && !cts.IsCancellationRequested; i++)
    {
        string text = $"Сервер обновлен. Уведомление №{i} ({DateTime.Now:HH:mm:ss})";
        byte[] data = Encoding.UTF8.GetBytes(text);
        await server.SendAsync(data, data.Length, groupEp);
        ConsoleLog.Write("СЕРВЕР", $"отправлено в группу: \"{text}\"");
        if (i < NotificationCount)
            await Task.Delay(NotificationIntervalMs, cts.Token);
    }

    ConsoleLog.Write("СЕРВЕР", "рассылка уведомлений завершена");
}
Console.WriteLine();

// --- Фаза 2: групповой чат — каждый клиент отправляет сообщение в группу ---
Console.WriteLine("--- Фаза 2. Групповой чат: каждый клиент шлёт в группу ---");
foreach (MulticastClient client in clients)
{
    await client.SendChatAsync($"Привет всем от {client.Name}! (групповой чат)");
    await Task.Delay(400, cts.Token);
}

await Task.Delay(800, cts.Token);

// --- Завершение ---
cts.Cancel();
try { await Task.WhenAll(receiveTasks); } catch { }
foreach (MulticastClient client in clients) client.Close();

Console.WriteLine();
Console.WriteLine("=========================================================");
Console.WriteLine("                   РАБОТА ЗАВЕРШЕНА");
Console.WriteLine("=========================================================");

// ------------------------------------------------------------
//  Логирование в консоль
// ------------------------------------------------------------
static class ConsoleLog
{
    public static void Write(string who, string text) =>
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {who,-9} {text}");
}

// ------------------------------------------------------------
//  Multicast-клиент: подписка на группу и приём сообщений
// ------------------------------------------------------------
sealed class MulticastClient
{
    private readonly UdpClient _udp;
    private readonly IPAddress _group;
    private readonly IPEndPoint _groupEndPoint;

    public string Name { get; }

    public MulticastClient(string name, IPAddress group, int port)
    {
        Name = name;
        _group = group;
        _groupEndPoint = new IPEndPoint(group, port);

        _udp = new UdpClient();
        // ReuseAddress позволяет нескольким клиентам на одной машине
        // слушать один и тот же порт группы одновременно.
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        _udp.MulticastLoopback = true;
    }

    // Подписка на multicast-группу
    public void Join()
    {
        _udp.JoinMulticastGroup(_group);
        ConsoleLog.Write(Name, $"подписан на группу {_group}:{_groupEndPoint.Port}");
    }

    // Отписка и освобождение ресурсов
    public void Close()
    {
        try { _udp.DropMulticastGroup(_group); } catch { /* сокет уже закрыт */ }
        _udp.Dispose();
        ConsoleLog.Write(Name, "отключён от группы");
    }

    // Цикл приёма сообщений из группы
    public async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result = await _udp.ReceiveAsync(ct);
                string text = Encoding.UTF8.GetString(result.Buffer);
                ConsoleLog.Write(Name, $"принято от {result.RemoteEndPoint}: \"{text}\"");
            }
        }
        catch (OperationCanceledException) { /* штатное завершение приёма */ }
        catch (ObjectDisposedException) { /* сокет закрыт при завершении */ }
    }

    // Отправка сообщения в группу (групповой чат)
    public async Task SendChatAsync(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        await _udp.SendAsync(data, data.Length, _groupEndPoint);
        ConsoleLog.Write(Name, $"отправлено в группу: \"{text}\"");
    }
}
