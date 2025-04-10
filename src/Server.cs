using System.Net;
using System.Net.Sockets;

Request parse(Stream stream)
{
  StreamReader reader = new(stream); // DON'T DISPOSE
  string requestLine = reader.ReadLine()!;
  if (requestLine.Split(' ') is not [string method, string path, string protocol])
  {
    throw new FormatException("Invalid request line format");
  }

  Dictionary<string, string> headers = new();
  string? line;
  while ((line = reader.ReadLine()) != null && line != "")
  {
    if (line.Split(':', 2) is [string key, string value])
    {
      headers[key.Trim()] = value.Trim();
    }
    else
    {
      throw new FormatException("Invalid header format");
    }
  }

  if (method != "GET")
  {
    throw new NotImplementedException("Only GET method is implemented");
  }
  // TODO understand how to not hang here string body = reader.ReadToEnd();
  string body = "";

  return new Request(method, path, protocol, headers, body);
}

void Handle(Stream stream, Response response)
{
  StreamWriter writer = new(stream) {
    NewLine = "\r\n",
  };
  writer.WriteLine($"{response.Protocol} {response.StatusCode} {response.StatusMessage}");
  foreach (var header in response.Headers)
  {
    writer.WriteLine($"{header.Key}: {header.Value}");
  }
  writer.WriteLine();
  writer.Write(response.Body);
  writer.Flush();
}

using TcpListener server = new(IPAddress.Any, 4221);
server.Start();
using Socket client = server.AcceptSocket();
Console.WriteLine("Client connected");

using NetworkStream stream = new(client);
var request = parse(stream);
Console.WriteLine("Received: " + request.ToString().Replace(", ", ",\n  "));

var response = new Response(
    request.Protocol,
    request.Path == "/" ? 200 : 404,
    request.Path == "/" ? "OK" : "Not Found",
    new Dictionary<string, string> { { "Content-Type", "text/plain" } },
    "Hello, World!"
);
Console.WriteLine("Sending response:\n" + response.ToString().Replace(", ", ",\n  "));

Handle(stream, response);

record Request(string Method, string Path, string Protocol, Dictionary<string, string> Headers, string Body);
record Response(string Protocol, int StatusCode, string StatusMessage, Dictionary<string, string> Headers, string Body);
