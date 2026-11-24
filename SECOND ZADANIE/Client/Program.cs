using System.Net.Sockets;
using System.Text;

Console.Write("Enter string: ");
string input = Console.ReadLine() ?? "Hello";

using var client = new TcpClient();
client.Connect("127.0.0.1", 65432);

using var stream = client.GetStream();
byte[] data = Encoding.UTF8.GetBytes(input);
stream.Write(data, 0, data.Length);
Console.WriteLine($"Sent: {input}");

byte[] buf = new byte[1024];
int n = stream.Read(buf, 0, buf.Length);
Console.WriteLine($"Received: {Encoding.UTF8.GetString(buf, 0, n)}");
