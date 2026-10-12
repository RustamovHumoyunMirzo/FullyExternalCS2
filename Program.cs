using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using System.Diagnostics;

namespace CS2Cheat;

public class Program
{
    private const string DumperExecutable = "cs2-dumper.exe";

    public static async Task Main(string[] args)
    {
        Console.WriteLine("[INFO] FullyExternalCS2 v2.0 (ImGui Edition)");
        var fresh = args.Any(arg => string.Equals(arg, "-fresh", StringComparison.OrdinalIgnoreCase));

        if (args.Any(arg => !string.Equals(arg, "-fresh", StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine("[WARN] Unknown arguments ignored. Use -fresh to run the bundled offset dumper.");

        var freshLoaded = false;
        if (fresh)
        {
            Console.WriteLine("[INFO] -fresh selected. Waiting for cs2.exe before running cs2-dumper...");
            await WaitForCs2Process();
            freshLoaded = await TryRunFreshDumper();
            if (!freshLoaded)
                Console.WriteLine("[WARN] Fresh offset update unavailable. Falling back to the existing online/cache offset flow.");
        }

        if (!freshLoaded)
        {
            Console.WriteLine("[INFO] Updating offsets...");
            if (!await Offsets.UpdateOffsets())
            {
                Console.WriteLine("[INFO] Process finished.");
                return;
            }
        }
        Console.WriteLine("[INFO] Offsets ready.");

        Console.WriteLine("[INFO] Waiting for CS2 process...");
        var gameProcess = new GameProcess();
        gameProcess.Start();

        var lastStatus = string.Empty;
        while (!gameProcess.IsValid || gameProcess.WindowRectangleClient.Width <= 0)
        {
            Thread.Sleep(500);
            if (!string.Equals(lastStatus, gameProcess.Status, StringComparison.Ordinal))
            {
                lastStatus = gameProcess.Status;
                Console.WriteLine();
                Console.WriteLine($"[INFO] {lastStatus}");
            }
            else
            {
                Console.Write(".");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"[INFO] CS2 found. Window: {gameProcess.WindowRectangleClient.Width}x{gameProcess.WindowRectangleClient.Height}");

        var gameData = new GameData(gameProcess);
        gameData.Start();

        Thread.Sleep(2000);

        Console.WriteLine("[INFO] Starting overlay...");

        var overlay = new OverlayRenderer(gameProcess, gameData);
        overlay.StartFeatures();

        Console.WriteLine("[INFO] Overlay started. Press INSERT to toggle menu.");
        Console.WriteLine("[INFO] Close this console window to exit.");

        await overlay.Run();

        overlay.StopFeatures();
        gameData.Dispose();
        gameProcess.Dispose();
    }

    private static async Task WaitForCs2Process()
    {
        var lastNotice = DateTime.UtcNow;
        while (System.Diagnostics.Process.GetProcessesByName("cs2").Length == 0)
        {
            if ((DateTime.UtcNow - lastNotice).TotalSeconds >= 10)
            {
                Console.WriteLine("[INFO] Still waiting for cs2.exe. Start CS2 to continue.");
                lastNotice = DateTime.UtcNow;
            }
            await Task.Delay(500);
        }
        Console.WriteLine("[INFO] cs2.exe detected.");
    }

    private static async Task<bool> TryRunFreshDumper()
    {
        try
        {
            var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "assets");
            var dumperPath = Path.Combine(assetsDirectory, DumperExecutable);
            var outputDirectory = Path.Combine(assetsDirectory, "offsets_output");
            if (!File.Exists(dumperPath))
            {
                Console.WriteLine($"[ERROR] Bundled dumper was not found: {dumperPath}");
                return false;
            }

            Directory.CreateDirectory(outputDirectory);
            foreach (var fileName in new[] { "offsets.json", "client_dll.json" })
            {
                var previousOutput = Path.Combine(outputDirectory, fileName);
                if (File.Exists(previousOutput)) File.Delete(previousOutput);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = dumperPath,
                WorkingDirectory = assetsDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputDirectory);
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add("cs2.exe");
            startInfo.ArgumentList.Add("-n");

            Console.WriteLine("[INFO] Running bundled cs2-dumper. This can take a moment...");
            using var dumper = System.Diagnostics.Process.Start(startInfo);
            if (dumper == null)
            {
                Console.WriteLine("[ERROR] Windows could not start cs2-dumper.");
                return false;
            }

            var stdoutTask = ForwardDumperOutput(dumper.StandardOutput, "[DUMPER]");
            var stderrTask = ForwardDumperOutput(dumper.StandardError, "[DUMPER]");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await dumper.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                dumper.Kill(entireProcessTree: true);
                Console.WriteLine("[ERROR] cs2-dumper timed out after two minutes.");
                return false;
            }
            await Task.WhenAll(stdoutTask, stderrTask);
            if (dumper.ExitCode != 0)
            {
                Console.WriteLine($"[ERROR] cs2-dumper exited with code {dumper.ExitCode}.");
                Console.WriteLine("[INFO] Check that CS2 is running and try running this program as administrator.");
                return false;
            }

            return Offsets.LoadOffsetsFromDirectory(outputDirectory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not refresh offsets: {ex.Message}");
            Console.WriteLine("[INFO] Try running this program as administrator with CS2 already open.");
            return false;
        }
    }

    private static async Task ForwardDumperOutput(StreamReader stream, string prefix)
    {
        while (await stream.ReadLineAsync() is { } line)
            if (!string.IsNullOrWhiteSpace(line)) Console.WriteLine($"{prefix} {line}");
    }
}
