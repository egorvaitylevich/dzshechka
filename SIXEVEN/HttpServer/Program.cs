using System.Net;
using System.Net.Sockets;
using System.Text;

// ============================================================
//  Простейший HTTP-сервер на сокетах
//
//  Слушает порт 8080 и на любой запрос отдаёт статическую
//  HTML-страницу «Привет, мир!».
// ============================================================

const int Port = 8080;

string html =
    """
    <!DOCTYPE html>
    <html lang="ru">
    <head>
        <meta charset="UTF-8">
        <title>Привет, мир!</title>
        <style>
            body { font-family: system-ui, sans-serif; display: flex;
                   justify-content: center; align-items: center;
                   height: 100vh; margin: 0; background: #f0f4f8; }
            h1  { color: #1a365d; font-size: 2.5rem; }
        </style>
    </head>
    <body>
        <h1>Привет, мир!</h1>
    </body>
    </html>
    """;

byte[] bodyBytes = Encoding.UTF8.GetBytes(html);

// Собираем HTTP-ответ
string responseHeaders =
    "HTTP/1.1 200 OK\r\n" +
    "Content-Type: text/html; charset=utf-8\r\n" +
    $"Content-Length: {bodyBytes.Length}\r\n" +
    "Connection: close\r\n" +
    "\r\n";

byte[] headerBytes = Encoding.ASCII.GetBytes(responseHeaders);

var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();

Console.WriteLine("=========================================================");
Console.WriteLine("     HTTP-СЕРВЕР НА СОКЕТАХ");
Console.WriteLine("=========================================================");
Console.WriteLine($"Слушает порт {Port}");
Console.WriteLine("Откройте в браузере: http://localhost:8080/");
Console.WriteLine("Или выполните: curl http://localhost:8080/");
Console.WriteLine("Для остановки нажмите Ctrl+C");
Console.WriteLine();

try
{
    while (true)
    {
        // Принимаем входящее TCP-соединение
        using TcpClient client = await listener.AcceptTcpClientAsync();
        using NetworkStream stream = client.GetStream();

        // Читаем запрос (нам достаточно первой строки и заголовков)
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {client.Client.RemoteEndPoint} → {requestLine}");

        // Пропускаем остальные заголовки запроса до пустой строки
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line))
                break;
        }

        // Отправляем ответ: заголовки + тело
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(bodyBytes);
        await stream.FlushAsync();
    }
}
catch (OperationCanceledException)
{
    // нормальное завершение
}
finally
{
    listener.Stop();
    Console.WriteLine("Сервер остановлен.");
}
