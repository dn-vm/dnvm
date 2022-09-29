
using System.Net;

namespace Dnvm.Test;

internal sealed class MockServer
{
    private readonly HttpListener _listener;
    private readonly List<Task> _tasks = new List<Task>();
    public int Port { get; }

    public MockServer()
    {
        // Generate a temporary port for testing
        while (true)
        {
            Port = Random.Shared.Next(49152, 65535);
            _listener = new HttpListener();
            try
            {
                _listener.Prefixes.Add($"http://localhost:{Port}");
                _listener.Start();
                _listener.GetContextAsync().ContinueWith(HandleConnection);
                return;
            }
            catch
            {
            }
        }
    }

    private async Task HandleConnection(Task<HttpListenerContext> listenerTask)
    {
        var listenerCtx = await listenerTask;
        var req = listenerCtx.Request;
        var response = listenerCtx.Response;
        var url = req.Url;
        string responseString = "<HTML><BODY> Hello world!</BODY></HTML>";
        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
        // Get a response stream and write the response to it.
        response.ContentLength64 = buffer.Length;
        var output = response.OutputStream;
        output.Write(buffer, 0, buffer.Length);
        // You must close the output stream.
        output.Close();
    }
}