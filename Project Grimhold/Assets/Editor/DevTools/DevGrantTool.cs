using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Editor side of the dev grant tool (odd/tasks/dev-grant-tools.md): builds the arguments for
/// tools/dev-grant/grant.js, runs it with node and, in Play Mode, rereads the profile. The script owns every
/// rule (catalog, caps, placement, local-database guard); this class only validates the form input.
/// </summary>
public sealed class DevGrantTool
{
    public const string ScriptPath = "tools/dev-grant/grant.js";
    public const int MaxItemAmount = 999;
    public const int MaxGold = 100000;
    public static readonly string[] Presets = { "upgrade-materials", "upgrade-gear" };

    private const string Prefix = "[dev-grant] ";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly Func<string[], Task<(int exitCode, string stdout, string stderr)>> _run;
    private readonly Func<Task<bool>> _reread;

    /// <param name="reread">Profile reread for Play Mode; null when the game is not running.</param>
    public DevGrantTool(Func<string[], Task<(int exitCode, string stdout, string stderr)>> run, Func<Task<bool>> reread)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _reread = reread;
    }

    public static string[] BuildArguments(string username, string preset, IReadOnlyList<(string lootId, int amount)> items, int gold)
    {
        string user = username?.Trim() ?? string.Empty;
        if (user.Length == 0) throw new ArgumentException("Indica el usuario de la cuenta.");
        if (HasSpaceOrQuote(user)) throw new ArgumentException("El usuario no puede tener espacios ni comillas.");
        return Build("--user", user, preset, items, gold);
    }

    /// <summary>Targets the character of the running Play Mode session (ApplicationAuthContext.CharacterId).</summary>
    public static string[] BuildArgumentsForCharacter(string characterId, string preset, IReadOnlyList<(string lootId, int amount)> items, int gold)
    {
        if (string.IsNullOrEmpty(characterId)) throw new ArgumentException("Entra en Play Mode con un personaje para usar los botones rápidos.");
        if (!EquipmentInstance.IsValidId(characterId)) throw new ArgumentException($"Id de personaje inválido: {characterId}.");
        return Build("--character", characterId, preset, items, gold);
    }

    private static string[] Build(string targetOption, string target, string preset, IReadOnlyList<(string lootId, int amount)> items, int gold)
    {
        if (!string.IsNullOrEmpty(preset) && Array.IndexOf(Presets, preset) < 0) throw new ArgumentException($"Preset desconocido: {preset}.");
        if (gold < 0 || gold > MaxGold) throw new ArgumentException($"El oro debe estar entre 0 y {MaxGold}.");

        var args = new List<string> { ScriptPath, targetOption, target };
        if (!string.IsNullOrEmpty(preset)) { args.Add("--preset"); args.Add(preset); }
        foreach (var (lootId, amount) in items ?? Array.Empty<(string, int)>())
        {
            string id = lootId?.Trim() ?? string.Empty;
            if (id.Length == 0 || id.Contains(":") || HasSpaceOrQuote(id) || amount < 1 || amount > MaxItemAmount)
                throw new ArgumentException($"Ítem inválido: '{lootId}' x{amount} (lootId sin espacios, cantidad 1..{MaxItemAmount}).");
            args.Add("--item");
            args.Add($"{id}:{amount}");
        }
        if (gold > 0) { args.Add("--gold"); args.Add(gold.ToString()); }
        if (args.Count == 3) throw new ArgumentException("Elige un preset, ítems u oro.");
        return args.ToArray();
    }

    public static DevGrantOutcome Interpret(int exitCode, string stdout, string stderr)
    {
        if (exitCode == 0) return new DevGrantOutcome(true, FirstLine(stdout, Prefix + "OK") ?? "OK");
        string error = LastLine(stderr, Prefix);
        return new DevGrantOutcome(false, error ?? $"El script terminó con código {exitCode} sin mensaje.");
    }

    public async Task<string> RunAsync(string[] arguments)
    {
        var (exitCode, stdout, stderr) = await _run(arguments);
        var outcome = Interpret(exitCode, stdout, stderr);
        if (!outcome.Success) return "Error: " + outcome.Message;
        if (_reread == null) return outcome.Message + ". Entra al Pueblo para ver los objetos.";
        return await _reread()
            ? outcome.Message + ". Inventario sincronizado."
            : outcome.Message + ", pero no se pudo sincronizar el inventario. Vuelve a entrar al Pueblo.";
    }

    /// <summary>Runs node with the given arguments in the repository root and captures both streams.</summary>
    public static Task<(int exitCode, string stdout, string stderr)> RunNodeAsync(string workingDirectory, string node, string[] arguments) =>
        Task.Run(() =>
        {
            var info = new ProcessStartInfo(string.IsNullOrWhiteSpace(node) ? "node" : node, Join(arguments))
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            try
            {
                using var process = Process.Start(info);
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
                {
                    process.Kill();
                    return (-1, stdout.Result, Prefix + "TIMEOUT: el script no terminó en " + Timeout.TotalSeconds + " s.");
                }
                process.WaitForExit();
                return (process.ExitCode, stdout.Result, stderr.Result);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                return (-1, string.Empty, Prefix + "NODE_NOT_FOUND: no se pudo ejecutar '" + info.FileName + "' (" + exception.Message + ").");
            }
        });

    private static bool HasSpaceOrQuote(string value)
    {
        foreach (char character in value) if (char.IsWhiteSpace(character) || character == '"') return true;
        return false;
    }

    private static string Join(IEnumerable<string> arguments)
    {
        var builder = new StringBuilder();
        foreach (string argument in arguments)
        {
            if (builder.Length > 0) builder.Append(' ');
            builder.Append('"').Append(argument).Append('"');
        }
        return builder.ToString();
    }

    private static string FirstLine(string text, string prefix)
    {
        foreach (string line in Lines(text)) if (line.StartsWith(prefix, StringComparison.Ordinal)) return line.Substring(Prefix.Length);
        return null;
    }

    private static string LastLine(string text, string prefix)
    {
        string found = null;
        foreach (string line in Lines(text)) if (line.StartsWith(prefix, StringComparison.Ordinal)) found = line.Substring(Prefix.Length);
        return found;
    }

    private static IEnumerable<string> Lines(string text) =>
        (text ?? string.Empty).Replace("\r", string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
}
