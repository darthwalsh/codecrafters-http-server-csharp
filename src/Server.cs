using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

string? directory;
if (args.Length == 0) {
  directory = null;
}
else if (args.Length == 2 && args[0] == "--directory") {
  directory = args[1];
}
else {
  Console.WriteLine("Usage: dotnet run -- --directory <directory>");
  return 1;
}

string GetDirectory() {
  if (directory == null) {
    throw new ArgumentException("Directory not specified");
  }
  return directory;
}

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
  // TODO(POST) understand how to not hang here string body = reader.ReadToEndAsync();
  return new Request(method, path, protocol, headers, new byte[0]);
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
  await writer.FlushAsync();
  await stream.WriteAsync(response.Body.AsMemory(0, response.Body.Length));
  await stream.FlushAsync();
}

Task<Response> Ok(string body) {
  return Task.FromResult(OkBytes(Encoding.UTF8.GetBytes(body), "text/plain"));
}

Response OkBytes(byte[] body, string contentType) {
  Dictionary<string, string> headers = new() { ["Content-Type"] = contentType, ["Content-Length"] = body.Length.ToString() };
  return new Response("HTTP/1.1", 200, "OK", headers, body);
}

var notFound = new Response(
  "HTTP/1.1",
  404,
  "Not Found",
  new Dictionary<string, string> { { "Content-Type", "text/plain" } },
  Encoding.UTF8.GetBytes("Not Found")
);

bool IsValidFilename(string filename) {
  int index = filename.IndexOfAny(Path.GetInvalidFileNameChars());
  bool isValid = index == -1;
  if (!isValid) {
    Console.WriteLine($"Invalid filename: {filename} at {index}: {filename[index]}");
  }
  return isValid;
}

async Task<Response> GetFile(string filename) {
  if (!IsValidFilename(filename)) return notFound;

  string path = Path.Combine(GetDirectory(), filename);
  if (!File.Exists(path)) {
    return notFound;
  }
  return OkBytes(await File.ReadAllBytesAsync(path), "application/octet-stream");
}

async Task<Response> WriteFile(string filename, byte[] body) {
  if (!IsValidFilename(filename)) return notFound;

  string path = Path.Combine(GetDirectory(), filename);
  await File.WriteAllBytesAsync(path, body);
  return new Response(
    "HTTP/1.1",
    201,
    "Created",
    new Dictionary<string, string> { { "Content-Type", "text/plain" } },
    Encoding.UTF8.GetBytes("Created")
  );
}

var routes = new Dictionary<string, List<(string, Func<Request, Task<Response>>)>> {
  ["GET"] = [
      ("/", (request) => Ok("Hello, World!")),
      ("/echo/.*", (request) => Ok(request.Path[6..])),
      ("/files/.*", (request) => GetFile(request.Path[7..])),
      ("/user-agent", (request) => Ok(request.Headers["User-Agent"])),
  ],
  ["POST"] = [
      ("/files.*", (request) => WriteFile(request.Path[7..], request.Body)),
  ],
};


async Task HandleClient(Socket client) {
  using Socket socket = client;
  using NetworkStream stream = new(socket);
  Request request = await parse(stream);
  Console.WriteLine("Received: " + request.ToString().Replace(", ", ",\n  "));

  if (!routes.TryGetValue(request.Method, out var methodRoutes)) {
    await Handle(stream, notFound);
    return;
  }

  foreach ((string pattern, var func) in methodRoutes) {
    if (!Regex.IsMatch(request.Path, $"^{pattern}$")) {
      Console.WriteLine($"Path {request.Path} does not match {pattern}");
      continue;
    }

    Response response = await func(request);
    Console.WriteLine("Sending response:\n" + response.ToString().Replace(", ", ",\n  "));
    await Handle(stream, response);
    return;
  }
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
    Console.WriteLine("Client disconnected\n");
  });
}

record Request(string Method, string Path, string Protocol, Dictionary<string, string> Headers, byte[] Body);
record Response(string Protocol, int StatusCode, string StatusMessage, Dictionary<string, string> Headers, byte[] Body);
