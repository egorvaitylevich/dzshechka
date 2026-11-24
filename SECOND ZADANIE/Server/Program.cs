using System.Net;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 65432);
listener.Start();
Console.WriteLine("Server started on 127.0.0.1:65432");

using var client = listener.AcceptTcpClient();
Console.WriteLine($"Client: {client.Client.RemoteEndPoint}");

using var stream = client.GetStream();
byte[] buf = new byte[1024];
int n = stream.Read(buf, 0, buf.Length);

string received = Encoding.UTF8.GetString(buf, 0, n);
Console.WriteLine($"Received: {received}");

string response = received.ToUpperInvariant();
byte[] resp = Encoding.UTF8.GetBytes(response);
stream.Write(resp, 0, resp.Length);
Console.WriteLine($"Sent: {response}");

listener.Stop();
