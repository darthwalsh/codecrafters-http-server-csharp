using System.Net;
using System.Net.Sockets;

using TcpListener server = new TcpListener(IPAddress.Any, 4221);
server.Start();
using Socket client = server.AcceptSocket();
Console.WriteLine("Client connected");

using NetworkStream stream = new NetworkStream(client);
byte[] buffer = new byte[1024];
int bytesRead = stream.Read(buffer, 0, buffer.Length);
string message = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
Console.WriteLine("Received: " + message);


string response = "HTTP/1.1 200 OK\r\n\r\n";
byte[] responseBytes = System.Text.Encoding.UTF8.GetBytes(response);
stream.Write(responseBytes, 0, responseBytes.Length);

