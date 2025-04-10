using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

async Task<Request> parse(Stream stream) {
  StreamReader reader = new(stream); // DON'T DISPOSE
  string? requestLine = await reader.ReadLineAsync();
  if (requestLine!.Split(' ') is not [string method, string path, string protocol]) {
    throw new FormatException("Invalid request line format");
  }

  Dictionary<string, string> headers = new();
  string? line;
  while ((line = await reader.ReadLineAsync()) != null && line != "") {
    if (line.Split(':', 2) is [string key, string value]) {
      headers[key.Trim()] = value.Trim();
    } else {
      throw new FormatException("Invalid header format");
    }
  }

  if (method != "GET") {
    throw new NotImplementedException("Only GET method is implemented");
  }
  // TODO understand how to not hang here string body = reader.ReadToEndAsync();
  string body = "";

  return new Request(method, path, protocol, headers, body);
}

async Task Handle(Stream stream, Response response) {
  StreamWriter writer = new(stream) {
    NewLine = "\r\n",
  };
  await writer.WriteLineAsync($"{response.Protocol} {response.StatusCode} {response.StatusMessage}");
  foreach (KeyValuePair<string, string> header in response.Headers) {
    await writer.WriteLineAsync($"{header.Key}: {header.Value}");
  }
  await writer.WriteLineAsync();
  await writer.WriteAsync(response.Body);
  await writer.FlushAsync();
}

Response Ok(string body) {
  Dictionary<string, string> headers = new() { ["Content-Type"] = "text/plain", ["Content-Length"] = body.Length.ToString() };
  return new Response("HTTP/1.1", 200, "OK", headers, body);
}

List<(string, Func<Request, Response>)> routes =
[
  ("/", (request) => Ok("Hello, World!")),
  ("/echo/.*", (request) => Ok(request.Path[6..])),
  ("/user-agent", (request) => Ok(request.Headers["User-Agent"])),
];

async Task HandleClient(Socket client) {
  using Socket socket = client;
  using NetworkStream stream = new(socket);
  Request request = await parse(stream);
  Console.WriteLine("Received: " + request.ToString().Replace(", ", ",\n  "));

  foreach ((string pattern, Func<Request, Response> func) in routes) {
    if (!Regex.IsMatch(request.Path, $"^{pattern}$")) {
      Console.WriteLine($"Path {request.Path} does not match {pattern}");
      continue;
    }

    Response response = func(request);
    Console.WriteLine("Sending response:\n" + response.ToString().Replace(", ", ",\n  "));
    await Handle(stream, response);
    return;
  }
  Response notFound = new(
    request.Protocol,
    404,
    "Not Found",
    new Dictionary<string, string> { { "Content-Type", "text/plain" } },
    "Not Found"
  );
  await Handle(stream, notFound);
}

using TcpListener server = new(IPAddress.Any, 4221);
server.Start();
while (true) {
  Console.WriteLine("Waiting for a connection...");
  Socket client = server.AcceptSocket();
  Console.WriteLine("Client connected");
  Task result = HandleClient(client);
  _ = result.ContinueWith((t) => {
    if (t.IsFaulted) {
      Console.Error.WriteLine("Error: " + t.Exception);
      return;
    }
    Console.WriteLine("Client disconnected");
  });
}

record Request(string Method, string Path, string Protocol, Dictionary<string, string> Headers, string Body);
record Response(string Protocol, int StatusCode, string StatusMessage, Dictionary<string, string> Headers, string Body);
