using System.Net.Sockets;
using System.Text;

// ============================================================
//  HTTP-клиент на сокетах (без HttpClient / WebRequest / requests)
//
//  Делает GET-запрос к указанному хосту и выводит в консоль
//  заголовки ответа и тело (HTML).
// ============================================================

Console.WriteLine("=========================================================");
Console.WriteLine("     HTTP-КЛИЕНТ НА СОКЕТАХ");
Console.WriteLine("=========================================================");
Console.WriteLine();

// Хост и путь можно задать аргументами: dotnet run -- example.com /path
string host = args.Length > 0 ? args[0] : "example.com";
string path = args.Length > 1 ? args[1] : "/";
int port = 80;

Console.WriteLine($"Запрос: GET http://{host}:{port}{path}");
Console.WriteLine();

try
{
    using var client = new TcpClient();
    // Устанавливаем TCP-соединение
    await client.ConnectAsync(host, port);
    Console.WriteLine($"[+] Подключено к {host}:{port}");

    using NetworkStream stream = client.GetStream();

    // Формируем HTTP/1.1 GET-запрос вручную
    // Host обязателен в HTTP/1.1; Connection: close — чтобы сервер закрыл
    // соединение после ответа (проще читать до конца потока).
    var request = new StringBuilder();
    request.AppendLine($"GET {path} HTTP/1.1");
    request.AppendLine($"Host: {host}");
    request.AppendLine("User-Agent: SocketHttpClient/1.0");
    request.AppendLine("Accept: text/html, */*");
    request.AppendLine("Connection: close");
    request.AppendLine(); // пустая строка завершает заголовки

    byte[] requestBytes = Encoding.ASCII.GetBytes(request.ToString());
    await stream.WriteAsync(requestBytes);
    await stream.FlushAsync();

    Console.WriteLine("[+] Запрос отправлен");
    Console.WriteLine();
    Console.WriteLine("--- ОТВЕТ СЕРВЕРА ---");
    Console.WriteLine();

    // Читаем весь ответ (заголовки + тело)
    using var reader = new StreamReader(stream, Encoding.UTF8);
    string? response = await reader.ReadToEndAsync();

    if (string.IsNullOrEmpty(response))
    {
        Console.WriteLine("(пустой ответ)");
        return;
    }

    // Разделяем заголовки и тело по пустой строке
    int headerEnd = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
    if (headerEnd < 0)
        headerEnd = response.IndexOf("\n\n", StringComparison.Ordinal);

    if (headerEnd >= 0)
    {
        string headers = response.Substring(0, headerEnd).TrimEnd();
        string body = response.Substring(headerEnd).TrimStart('\r', '\n');

        Console.WriteLine("=== ЗАГОЛОВКИ ===");
        Console.WriteLine(headers);
        Console.WriteLine();
        Console.WriteLine("=== ТЕЛО (HTML) ===");
        Console.WriteLine(body);
    }
    else
    {
        // Не удалось разделить — выводим всё как есть
        Console.WriteLine(response);
    }
}
catch (SocketException ex)
{
    Console.WriteLine($"[!] Ошибка сокета: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"[!] Ошибка: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("Клиент завершил работу.");
