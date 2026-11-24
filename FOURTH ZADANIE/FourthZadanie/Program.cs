using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

// ============================================================
//  ЗАДАНИЕ №4. СРАВНЕНИЕ TCP И UDP
//  Передача сообщения и файла сначала по TCP, затем по UDP,
//  с замером времени передачи.
// ============================================================

const int Port = 65433;
const int UdpChunkSize = 1400;                 // полезная нагрузка одной UDP-датаграммы
const int FileSizeBytes = 10 * 1024 * 1024;    // тестовый файл 10 МБ

string workDir = AppContext.BaseDirectory;
string testFile = Path.Combine(workDir, "test_data.bin");
string receivedTcp = Path.Combine(workDir, "received_tcp.bin");
string receivedUdp = Path.Combine(workDir, "received_udp.bin");

// Готовим тестовый файл (детерминированное содержимое)
if (!File.Exists(testFile) || new FileInfo(testFile).Length != FileSizeBytes)
{
    CreateTestFile(testFile, FileSizeBytes);
}

string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

Console.WriteLine("=========================================================");
Console.WriteLine("          ЗАДАНИЕ №4. СРАВНЕНИЕ TCP И UDP");
Console.WriteLine("=========================================================");
Console.WriteLine($"Порт: {Port}");
Console.WriteLine($"Тестовый файл: {Path.GetFileName(testFile)} ({FileSizeBytes / (1024.0 * 1024):F0} МБ)");
Console.WriteLine($"UDP-датаграмма: {UdpChunkSize} байт полезной нагрузки");
Console.WriteLine();

if (mode is "all" or "tcp")
{
    await RunTcpDemo();
    Console.WriteLine();
}

if (mode is "all" or "udp")
{
    await RunUdpDemo();
    Console.WriteLine();
}

Console.WriteLine("=========================================================");
Console.WriteLine("                      РАБОТА ЗАВЕРШЕНА");
Console.WriteLine("=========================================================");


// ------------------------------------------------------------
//  ДЕМОНСТРАЦИЯ TCP
// ------------------------------------------------------------
async Task RunTcpDemo()
{
    Console.WriteLine("--------------- передача по TCP ---------------");

    var ready = new TaskCompletionSource();
    Task serverTask = Task.Run(() => TcpServerAsync(ready));
    await ready.Task;                 // ждём, пока сервер начнёт слушать

    await TcpClientAsync();           // запускаем клиента
    await serverTask;                 // ждём завершения сервера

    Console.WriteLine("--------------- TCP: завершено ----------------");
}

// Сервер: принимает сообщение и файл, подтверждает приём
async Task TcpServerAsync(TaskCompletionSource ready)
{
    using var listener = new TcpListener(IPAddress.Loopback, Port);
    listener.Start();
    ready.SetResult();
    Log("TCP-СЕРВЕР", $"Слушаю 127.0.0.1:{Port}. Ожидание подключения...");

    using TcpClient client = await listener.AcceptTcpClientAsync();
    Log("TCP-СЕРВЕР", $"Клиент подключён: {client.Client.RemoteEndPoint}");
    await using NetworkStream stream = client.GetStream();

    // 1) СООБЩЕНИЕ: [4 байта длина][данные]
    int msgLen = BitConverter.ToInt32(await ReadExactlyAsync(stream, 4));
    byte[] msgBytes = await ReadExactlyAsync(stream, msgLen);
    string msg = Encoding.UTF8.GetString(msgBytes);
    Log("TCP-СЕРВЕР", $"Получено сообщение ({msgLen} байт): \"{msg}\"");
    await stream.WriteAsync(Encoding.UTF8.GetBytes("OK"));
    Log("TCP-СЕРВЕР", "Отправлено подтверждение: OK");

    // 2) ФАЙЛ: [8 байт длина][данные]
    long fileLen = BitConverter.ToInt64(await ReadExactlyAsync(stream, 8));
    long received = 0;
    byte[] buffer = new byte[81920];
    await using (FileStream fs = File.Create(receivedTcp))
    {
        long remaining = fileLen;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = await stream.ReadAsync(buffer.AsMemory(0, toRead));
            if (read == 0) break;                 // клиент закрыл соединение досрочно
            await fs.WriteAsync(buffer.AsMemory(0, read));
            received += read;
            remaining -= read;
        }
    }
    Log("TCP-СЕРВЕР", $"Файл получен: {received} байт (ожидалось {fileLen})");
    await stream.WriteAsync(Encoding.UTF8.GetBytes("OK"));
    Log("TCP-СЕРВЕР", "Отправлено подтверждение: OK");

    listener.Stop();
}

// Клиент: устанавливает соединение, шлёт сообщение и файл, замеряет время
async Task TcpClientAsync()
{
    using var client = new TcpClient();

    Stopwatch connectSw = Stopwatch.StartNew();
    await client.ConnectAsync(IPAddress.Loopback, Port);
    connectSw.Stop();
    Log("TCP-КЛИЕНТ", $"Соединение установлено за {connectSw.Elapsed.TotalMilliseconds:F3} мс (3-стороннее рукопожатие)");

    await using NetworkStream stream = client.GetStream();

    // 1) СООБЩЕНИЕ
    string message = "Проверка передачи сообщения по TCP и UDP: надёжность и скорость.";
    byte[] msgBytes = Encoding.UTF8.GetBytes(message);

    Stopwatch msgSw = Stopwatch.StartNew();
    await stream.WriteAsync(BitConverter.GetBytes(msgBytes.Length));
    await stream.WriteAsync(msgBytes);
    await ReadExactIntoAsync(stream, new byte[2], 0, 2);   // ждём подтверждение
    msgSw.Stop();
    Log("TCP-КЛИЕНТ", $"Сообщение отправлено: \"{message}\"");
    Log("TCP-КЛИЕНТ", $"Подтверждение получено за {msgSw.Elapsed.TotalMilliseconds:F3} мс");

    // 2) ФАЙЛ
    long fileLen = new FileInfo(testFile).Length;
    Stopwatch fileSw = Stopwatch.StartNew();
    await stream.WriteAsync(BitConverter.GetBytes(fileLen));
    await using (FileStream fs = File.OpenRead(testFile))
    {
        await fs.CopyToAsync(stream, 81920);
    }
    await ReadExactIntoAsync(stream, new byte[2], 0, 2);   // ждём подтверждение
    fileSw.Stop();

    double sec = fileSw.Elapsed.TotalSeconds;
    double mbps = (fileLen / (1024.0 * 1024)) / sec;
    Log("TCP-КЛИЕНТ", $"Файл отправлен: {fileLen} байт за {fileSw.Elapsed.TotalMilliseconds:F1} мс ({mbps:F2} МБ/с)");
}


// ------------------------------------------------------------
//  ДЕМОНСТРАЦИЯ UDP
// ------------------------------------------------------------
async Task RunUdpDemo()
{
    Console.WriteLine("--------------- передача по UDP ---------------");

    using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
    var ready = new TaskCompletionSource();
    Task serverTask = Task.Run(() => UdpServerAsync(server, ready));
    await ready.Task;

    await UdpClientAsync();
    await serverTask;

    Console.WriteLine("--------------- UDP: завершено ----------------");
}

// Типы датаграмм воображаемого протокола:
//  1 = сообщение, 2 = заголовок файла, 3 = фрагмент файла, 4 = конец файла
// 10 = эхо-ответ (сервер->клиент), 11 = итог передачи (сервер->клиент)
async Task UdpServerAsync(UdpClient server, TaskCompletionSource ready)
{
    server.Client.ReceiveBufferSize = 8 * 1024 * 1024;   // большой буфер, чтобы не терять пакеты на приёме
    ready.SetResult();
    Log("UDP-СЕРВЕР", $"Слушаю 127.0.0.1:{Port}. Ожидание датаграмм...");

    long expectedTotal = 0;
    long receivedBytes = 0;
    HashSet<int> receivedSeqs = new();
    bool finished = false;

    while (!finished)
    {
        UdpReceiveResult result = await server.ReceiveAsync();
        byte[] data = result.Buffer;
        IPEndPoint remote = result.RemoteEndPoint;
        if (data.Length == 0) continue;

        switch (data[0])
        {
            case 1: // сообщение
                string msg = Encoding.UTF8.GetString(data, 1, data.Length - 1);
                Log("UDP-СЕРВЕР", $"Получено сообщение ({data.Length - 1} байт): \"{msg}\"");
                byte[] echo = (byte[])data.Clone();
                echo[0] = 10;
                await server.SendAsync(echo, echo.Length, remote);
                Log("UDP-СЕРВЕР", "Эхо-ответ отправлен");
                break;

            case 2: // заголовок файла
                expectedTotal = BitConverter.ToInt64(data, 1);
                Log("UDP-СЕРВЕР", $"Начало передачи файла, ожидается {expectedTotal} байт");
                break;

            case 3: // фрагмент файла
                int seq = BitConverter.ToInt32(data, 1);
                receivedSeqs.Add(seq);
                receivedBytes += data.Length - 5;
                break;

            case 4: // конец файла
                int expectedChunks = (int)Math.Ceiling(expectedTotal / (double)UdpChunkSize);
                int lost = expectedChunks - receivedSeqs.Count;
                Log("UDP-СЕРВЕР", $"Файл получен: {receivedBytes} байт из {expectedTotal} (потеряно пакетов: {lost})");
                byte[] summary = new byte[13];
                summary[0] = 11;
                BitConverter.GetBytes(receivedBytes).CopyTo(summary, 1);
                BitConverter.GetBytes(lost).CopyTo(summary, 9);
                await server.SendAsync(summary, summary.Length, remote);
                Log("UDP-СЕРВЕР", "Итог передачи отправлен клиенту");
                finished = true;
                break;
        }
    }
}

async Task UdpClientAsync()
{
    using var client = new UdpClient();
    client.Client.SendBufferSize = 1 * 1024 * 1024;
    var serverEp = new IPEndPoint(IPAddress.Loopback, Port);

    // 1) СООБЩЕНИЕ одним датаграммом (соединение не устанавливается)
    string message = "Проверка передачи сообщения по TCP и UDP: надёжность и скорость.";
    byte[] msgBytes = Encoding.UTF8.GetBytes(message);
    byte[] msgPacket = new byte[msgBytes.Length + 1];
    msgPacket[0] = 1;
    msgBytes.CopyTo(msgPacket, 1);

    Stopwatch msgSw = Stopwatch.StartNew();
    await client.SendAsync(msgPacket, msgPacket.Length, serverEp);
    UdpReceiveResult echo = await client.ReceiveAsync();
    msgSw.Stop();
    Log("UDP-КЛИЕНТ", $"Сообщение отправлено: \"{message}\"");
    Log("UDP-КЛИЕНТ", $"Эхо получено за {msgSw.Elapsed.TotalMilliseconds:F3} мс (соединение не устанавливалось)");

    // 2) ФАЙЛ, нарезанный на датаграммы
    long fileLen = new FileInfo(testFile).Length;
    byte[] meta = new byte[9];
    meta[0] = 2;
    BitConverter.GetBytes(fileLen).CopyTo(meta, 1);

    Stopwatch fileSw = Stopwatch.StartNew();
    await client.SendAsync(meta, meta.Length, serverEp);

    await using (FileStream fs = File.OpenRead(testFile))
    {
        byte[] chunk = new byte[UdpChunkSize];
        int seq = 0;
        int read;
        while ((read = await fs.ReadAsync(chunk)) > 0)
        {
            byte[] packet = new byte[5 + read];
            packet[0] = 3;
            BitConverter.GetBytes(seq).CopyTo(packet, 1);
            Array.Copy(chunk, 0, packet, 5, read);
            await client.SendAsync(packet, packet.Length, serverEp);
            seq++;
        }
    }
    await client.SendAsync(new byte[] { 4 }, 1, serverEp);   // конец файла

    UdpReceiveResult summary = await client.ReceiveAsync();
    fileSw.Stop();

    long recvBytes = BitConverter.ToInt64(summary.Buffer, 1);
    int lost = BitConverter.ToInt32(summary.Buffer, 9);
    double sec = fileSw.Elapsed.TotalSeconds;
    double mbps = (recvBytes / (1024.0 * 1024)) / sec;
    Log("UDP-КЛИЕНТ", $"Файл отправлен: {fileLen} байт за {fileSw.Elapsed.TotalMilliseconds:F1} мс");
    Log("UDP-КЛИЕНТ", $"Сервер получил: {recvBytes} байт, потеряно пакетов: {lost} ({mbps:F2} МБ/с)");
}


// ------------------------------------------------------------
//  Вспомогательные методы
// ------------------------------------------------------------
static void Log(string who, string text) =>
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {who,-10} {text}");

static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
{
    byte[] buffer = new byte[count];
    await ReadExactIntoAsync(stream, buffer, 0, count);
    return buffer;
}

static async Task ReadExactIntoAsync(NetworkStream stream, byte[] buffer, int offset, int count)
{
    int done = 0;
    while (done < count)
    {
        int read = await stream.ReadAsync(buffer.AsMemory(offset + done, count - done));
        if (read == 0) throw new EndOfStreamException("Соединение закрыто до получения всех данных.");
        done += read;
    }
}

static void CreateTestFile(string path, int size)
{
    using FileStream fs = File.Create(path);
    byte[] buffer = new byte[81920];
    Random rnd = new(42);                 // фиксированное зерно -> воспроизводимое содержимое
    rnd.NextBytes(buffer);
    int remaining = size;
    while (remaining > 0)
    {
        int n = Math.Min(buffer.Length, remaining);
        fs.Write(buffer, 0, n);
        remaining -= n;
    }
}
