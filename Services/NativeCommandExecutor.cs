using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using System;

namespace Privacy.Services;

internal sealed unsafe class NativeCommandExecutor
{
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    public NativeCommandExecutor(ICommandManager commandManager, IPluginLog log)
    {
        this.commandManager = commandManager;
        this.log = log;
    }

    public bool Execute(string command, bool suppressFailureLog = false)
    {
        if (string.IsNullOrWhiteSpace(command) || !command.StartsWith('/')) return false;

        log.Information("Privacy: executing command {Command}", command);

        // Lifestream's /li is a Dalamud plugin command, not a native game shell
        // command. Process it through ICommandManager first, then fall back to the
        // native shell for real game commands like /tell, /invite and /pcmd.
        if (TryProcessDalamudCommand(command, suppressFailureLog))
            return true;

        return ExecuteNative(command, suppressFailureLog);
    }


    public bool HasRegisteredCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var normalized = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        try
        {
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var property in commandManager.GetType().GetProperties(flags))
            {
                if (!property.Name.Contains("Command", StringComparison.OrdinalIgnoreCase) &&
                    !property.Name.Contains("Handler", StringComparison.OrdinalIgnoreCase))
                    continue;

                object? value;
                try { value = property.GetValue(commandManager); } catch { continue; }
                if (ContainsCommandKey(value, normalized)) return true;
            }

            foreach (var field in commandManager.GetType().GetFields(flags))
            {
                if (!field.Name.Contains("Command", StringComparison.OrdinalIgnoreCase) &&
                    !field.Name.Contains("Handler", StringComparison.OrdinalIgnoreCase))
                    continue;

                object? value;
                try { value = field.GetValue(commandManager); } catch { continue; }
                if (ContainsCommandKey(value, normalized)) return true;
            }
        }
        catch
        {
            // If Dalamud changes the internal command table, do not block the click.
            return true;
        }

        return false;
    }

    private static bool ContainsCommandKey(object? value, string command)
    {
        if (value == null) return false;

        if (value is System.Collections.IDictionary dictionary)
        {
            foreach (var key in dictionary.Keys)
            {
                if (string.Equals(key?.ToString(), command, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                if (item == null) continue;

                var keyProperty = item.GetType().GetProperty("Key");
                var key = keyProperty?.GetValue(item)?.ToString();
                if (string.Equals(key, command, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (string.Equals(item.ToString(), command, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private bool TryProcessDalamudCommand(string command, bool suppressFailureLog)
    {
        if (!IsDalamudOnlyCommand(command))
            return false;

        try
        {
            var method = commandManager.GetType().GetMethod("ProcessCommand", new[] { typeof(string) });
            if (method == null) return false;

            var result = method.Invoke(commandManager, new object[] { command });
            var handled = result is not bool value || value;
            log.Information("Privacy: Dalamud command {Command} handled={Handled}", command, handled);
            return handled;
        }
        catch (Exception ex)
        {
            if (!suppressFailureLog && IsDalamudOnlyCommand(command))
                log.Warning(ex, "Privacy failed to process Dalamud command: {Command}", command);
            return false;
        }
    }

    private bool ExecuteNative(string command, bool suppressFailureLog)
    {
        try
        {
            using var nativeCommand = new Utf8String(command);
            RaptureShellModule.Instance()->ExecuteCommandInner(&nativeCommand, UIModule.Instance());
            log.Information("Privacy: native command executed {Command}", command);
            return true;
        }
        catch (Exception ex)
        {
            if (!suppressFailureLog)
                log.Error(ex, "Privacy failed to execute native command: {Command}", command);
            return false;
        }
    }

    private static bool IsDalamudOnlyCommand(string command)
        => command.StartsWith("/li ", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(command.Trim(), "/li", StringComparison.OrdinalIgnoreCase);
}
