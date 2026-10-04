using System.Diagnostics;
using System.Text;
using M2Server.Lib.Domain;

namespace M2Server.Lib.Services;

public sealed class ScriptService
{
    private readonly object _gate = new();
    private readonly HashSet<Guid> _running = [];

    public async Task<string> RunAsync(ScriptEntry entry)
    {
        lock (this._gate)
        {
            if (!this._running.Add(entry.Id))
            {
                throw new InvalidOperationException("Script is already running.");
            }
        }

        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(entry.Script));
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var text = await output + await error;
            return $"Exit code {process.ExitCode}\r\n{text}";
        }
        finally
        {
            lock (this._gate)
            {
                this._running.Remove(entry.Id);
            }
        }
    }
}