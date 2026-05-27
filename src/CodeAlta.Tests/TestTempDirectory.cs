namespace CodeAlta.Tests;

internal sealed class TestTempDirectory(string path) : IDisposable
{
    public string Path { get; } = path;

    public static TestTempDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"codealta-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TestTempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
