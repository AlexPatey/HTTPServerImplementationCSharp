using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

var server = new TcpListener(IPAddress.Any, 4221);
server.Start();

while (true)
{
    //Wait for client
    var socket = server.AcceptSocket();

    Task.Run(() =>
    {
        //Instantiate and read request buffer
        var requestBuffer = new byte[1_024];
        _ = socket.Receive(requestBuffer);

        //Split the request into its three parts: [1] Request line; [2] Headers; [3] Request body
        var requestParts = Encoding.UTF8.GetString(requestBuffer).Split("\r\n");
        var requestLine = requestParts[0];
        var requestHeaders = requestParts.Skip(1).Take(requestParts.Length - 2).Where(h => h.Contains(':')).ToArray();
        var requestBody = requestParts[^1];

        //Split the request line into its three parts: [1] HTTP method; [2] Request target; [3] HTTP version
        var requestLineParts = requestLine.Split(' ');
        var (httpMethod, requestTarget, httpVersion) = (requestLineParts[0], requestLineParts[1], requestLineParts[2]);

        //Create a dictionary of request headers
        var requestHeadersDictionary = requestHeaders.ToDictionary(header => header.Split(':')[0], header => header.Split(':')[1].Trim());

        byte[] responseBuffer;

        if (requestTarget == "/")
        {
            responseBuffer =  "HTTP/1.1 200 OK\r\n\r\n"u8.ToArray(); /*The HTTP response is made up of three parts: [1] Status line: HTTP/1.1 200 OK\r\n, which is itself made up of into four parts: [a] HTTP/1.1: HTTP version; [b] 200: Status Code;
                                                                       [c] OK: Optional reason phrase; [d] \r\n: CRLF that marks the end of the status line; [2] Headers (which in this case is empty), followed by a CRLF which indicates the end
                                                                       of the Headers; [3] Optional response body: in this case empty.*/
        }
        else
        {
            string responseHeaders;
            
            //Split the request target into substrings, to extract the endpoint name, etc.
            var requestTargetParts = requestTarget.Split('/');
            var endpointName = requestTargetParts.ElementAtOrDefault(1);

            string? fileName;
            byte[]? file = null;
            string? directory;

            switch (endpointName)
            {
                case "echo":
                    var message = requestTargetParts.ElementAtOrDefault(2);
                    requestHeadersDictionary.TryGetValue("Accept-Encoding", out var acceptEncodingsHeader);
                    var acceptEncodings = acceptEncodingsHeader?.Split(',').Select(a => a.Trim()).ToArray();

                    if (acceptEncodings != null && acceptEncodings.Contains("gzip"))
                    {
                        var bytes = Encoding.UTF8.GetBytes(message);
                        using var memoryStream = new MemoryStream();
                        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, true);
                        
                        gzipStream.Write(bytes, 0, bytes.Length);
                        gzipStream.Flush();
                        gzipStream.Close();
                        
                        responseHeaders = $"Content-Type: text/plain\r\nContent-Encoding: gzip\r\nContent-Length: {memoryStream.ToArray().Length}\r\n";
                        responseBuffer = [..Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\n{responseHeaders}\r\n"), ..memoryStream.ToArray()];
                    }
                    else
                    {
                        responseHeaders = $"Content-Type: text/plain\r\nContent-Length: {message.Length}\r\n";
                        responseBuffer = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\n{responseHeaders}\r\n{message}");
                    }
                    break;
                case "user-agent":
                    var userAgent = requestHeadersDictionary["User-Agent"];
                    responseHeaders = $"Content-Type: text/plain\r\nContent-Length: {userAgent.Length}\r\n";
                    responseBuffer = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\n{responseHeaders}\r\n{userAgent}");
                    break;
                case "files" when httpMethod == "GET":
                    fileName = requestTargetParts.ElementAtOrDefault(2);
                    directory = Environment.GetCommandLineArgs()[2];
                    
                    var fileExists = File.Exists($"{directory}{fileName}");
                    
                    if (fileExists)
                    {
                        file = File.ReadAllBytes($"{directory}{fileName}");
                    }

                    if (file == null)
                    {
                        responseBuffer = Encoding.UTF8.GetBytes($"HTTP/1.1 404 Not Found\r\n\r\n");
                    }
                    else
                    {
                        responseHeaders = $"Content-Type: application/octet-stream\r\nContent-Length: {file.Length}\r\n";
                        var fileContents = Encoding.UTF8.GetString(file);
                        responseBuffer = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\n{responseHeaders}\r\n{fileContents}");
                    }
                    break;
                case "files" when httpMethod == "POST":
                    fileName = requestTargetParts.ElementAtOrDefault(2);
                    directory = Environment.GetCommandLineArgs()[2];
                    var contentLength = int.Parse(requestHeadersDictionary["Content-Length"]);
                    File.WriteAllText($"{directory}{fileName}", requestBody[..contentLength]);
                    responseBuffer = Encoding.UTF8.GetBytes($"HTTP/1.1 201 Created\r\n\r\n");
                    break;
                default:
                    responseBuffer =  "HTTP/1.1 404 Not Found\r\n\r\n"u8.ToArray();
                    break;
            }
        }

        socket.Send(responseBuffer);
    });
}