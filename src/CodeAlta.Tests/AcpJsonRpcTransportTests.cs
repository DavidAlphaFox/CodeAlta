using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using CodeAlta.Acp;

namespace CodeAlta.Tests;

[TestClass]
public sealed class AcpJsonRpcTransportTests
{
    [TestMethod]
    public async Task SendRequestAsync_WritesEnvelope_AndCorrelatesResponse()
    {
        var pipe = new Pipe();
        var clientInput = new MemoryStream();

        await using var transport = new AcpJsonRpcTransport(
            pipe.Reader.AsStream(),
            clientInput,
            AcpClient.CreateJsonSerializerOptions());

        var requestTask = transport.SendRequestAsync<InitializeRequest, InitializeResponse>(
            "initialize",
            new InitializeRequest
            {
                ClientInfo = new Implementation { Name = "test", Version = "1.0.0" },
                ClientCapabilities = new ClientCapabilities
                {
                    Auth = new AuthCapabilities(),
                    Fs = new FileSystemCapabilities(),
                },
                ProtocolVersion = new ProtocolVersion { Value = JsonSerializer.SerializeToElement("1") }
            });

        var response = """
                       {"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"1","agentCapabilities":{"auth":{},"mcpCapabilities":{"http":true,"sse":false},"promptCapabilities":{},"sessionCapabilities":{}}}}
                       """ + "\n";
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(response));
        await pipe.Writer.CompleteAsync();

        var result = await requestTask.ConfigureAwait(false);

        Assert.AreEqual("1", result.ProtocolVersion.Value.GetString());
        clientInput.Position = 0;
        var written = Encoding.UTF8.GetString(clientInput.ToArray());
        StringAssert.Contains(written, @"""jsonrpc"":""2.0""");
        StringAssert.Contains(written, @"""method"":""initialize""");
        StringAssert.Contains(written, @"""id"":1");
    }

    [TestMethod]
    public async Task StreamAsync_ReceivesServerNotificationAndRequest()
    {
        var serverOutput = new MemoryStream();
        var clientInput = new MemoryStream();

        var payload = """
                      {"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"session-1","update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Hello"}}}}
                      {"jsonrpc":"2.0","id":42,"method":"session/request_permission","params":{"sessionId":"session-1","toolCall":{"toolCallId":"tool-1","kind":"shell","title":"Run command","status":"pending"},"options":[]}}
                      """ + "\n";
        serverOutput.Write(Encoding.UTF8.GetBytes(payload));
        serverOutput.Position = 0;

        await using var transport = new AcpJsonRpcTransport(
            serverOutput,
            clientInput,
            AcpClient.CreateJsonSerializerOptions());

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var notification = await transport.Messages.ReadAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        var request = await transport.Messages.ReadAsync(cancellationTokenSource.Token).ConfigureAwait(false);

        Assert.AreEqual("session/update", notification.Method);
        Assert.IsNull(notification.RequestId);
        Assert.AreEqual("session/request_permission", request.Method);
        Assert.IsNotNull(request.RequestId);
    }

    [TestMethod]
    public async Task SendResponseAsync_WritesRawRequestId()
    {
        var serverOutput = new MemoryStream();
        var clientInput = new MemoryStream();

        await using var transport = new AcpJsonRpcTransport(
            serverOutput,
            clientInput,
            AcpClient.CreateJsonSerializerOptions());

        await transport.SendResponseAsync(
            new RequestId { Value = JsonSerializer.SerializeToElement(99) },
            new RequestPermissionResponse
            {
                Outcome = new RequestPermissionOutcome
                {
                    Value = JsonSerializer.SerializeToElement(new { outcome = "allow_once" })
                }
            });

        clientInput.Position = 0;
        var written = Encoding.UTF8.GetString(clientInput.ToArray());
        StringAssert.Contains(written, @"""id"":99");
        StringAssert.Contains(written, @"""outcome"":{""outcome"":""allow_once""}");
        Assert.IsFalse(written.Contains(@"""id"":{"), written);
        Assert.IsFalse(written.Contains(@"""value"""), written);
    }

    [TestMethod]
    public async Task DisposeAsync_CancelsPendingRequests()
    {
        var pipe = new Pipe();
        var clientInput = new MemoryStream();

        await using var transport = new AcpJsonRpcTransport(
            pipe.Reader.AsStream(),
            clientInput,
            AcpClient.CreateJsonSerializerOptions());

        var requestTask = transport.SendRequestAsync<InitializeRequest, InitializeResponse>(
            "initialize",
            new InitializeRequest
            {
                ClientInfo = new Implementation { Name = "test", Version = "1.0.0" },
                ClientCapabilities = new ClientCapabilities
                {
                    Auth = new AuthCapabilities(),
                    Fs = new FileSystemCapabilities(),
                },
                ProtocolVersion = new ProtocolVersion { Value = JsonSerializer.SerializeToElement("1") }
            });

        await transport.DisposeAsync();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => requestTask).ConfigureAwait(false);
    }
}
