using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace IntegrationTests;

public sealed class SagaIntegrationSkeletonTests
{
    [Fact]
    public async Task HealthEndpoints_ShouldExposeLivenessAndReadinessStates()
    {
        var port = GetFreeTcpPort();
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var apiProjectPath = Path.Combine(repositoryRoot, "src", "Api", "Api.csproj");
        var process = StartApiProcess(apiProjectPath, port);

        try
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}")
            };

            await WaitForStatusCodeAsync(httpClient, process, "/health/live", HttpStatusCode.OK, TimeSpan.FromSeconds(60));

            var liveness = await httpClient.GetAsync("/health/live");
            var readiness = await httpClient.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Fact(Skip = "Requires RabbitMQ wiring plus participant services to validate the full orchestrated saga flow.")]
    public Task Workflow_ShouldPersistSagaStateAcrossPublishedEvents()
    {
        return Task.CompletedTask;
    }

    private static Process StartApiProcess(string apiProjectPath, int port)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --configuration Release --no-launch-profile --project \"{apiProjectPath}\"",
                WorkingDirectory = Path.GetDirectoryName(apiProjectPath)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.StartInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        process.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        process.Start();
        return process;
    }

    private static async Task WaitForStatusCodeAsync(
        HttpClient httpClient,
        Process process,
        string path,
        HttpStatusCode expectedStatusCode,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await httpClient.GetAsync(path);
                if (response.StatusCode == expectedStatusCode)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(500);
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        throw new TimeoutException(
            $"Endpoint {path} did not return {(int)expectedStatusCode} within {timeout.TotalSeconds} seconds. stdout: {output} stderr: {error}");
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
