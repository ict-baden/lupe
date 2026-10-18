using Lupe.Core.Input;
using Lupe.Core.Platform;
using Lupe.Core.Resources;
using Lupe.Platform.Linux.Portal;
using Tmds.DBus.Protocol;

namespace Lupe.Platform.Linux.Input;

/// <summary>
/// Tippen unter Wayland über das RemoteDesktop-Portal (<c>NotifyKeyboardKeysym</c>). Beim ersten Mal fragt der Desktop,
/// ob Lupe die Tastatur steuern darf; das Wiederherstellungs-Token wird unter ~/.config/lupe gespeichert, damit die
/// Frage nicht bei jedem Start kommt (sofern das Portal es unterstützt, ab Version 2).
/// </summary>
internal sealed class PortalTyper : IDisposable
{
    private const string Iface = "org.freedesktop.portal.RemoteDesktop";
    private const uint DeviceKeyboard = 1;
    private const uint PersistUntilRevoked = 2;

    private readonly Lock _gate = new();
    private PortalConnection? _portal;
    private string? _session;
    private string? _lastError;

    /// <summary>Datei mit dem Wiederherstellungs-Token.</summary>
    internal static string TokenPath => Path.Combine(LinuxSession.LupeConfigDir, "remotedesktop-token");

    /// <summary>Stellt die Sitzung her (kann einen Dialog des Desktops zeigen) und meldet, was fehlt; null = bereit.</summary>
    public string? Problem()
    {
        lock (_gate)
        {
            if (_session is not null) return null;
            try
            {
                Run(StartAsync);
                return null;
            }
            catch (PortalException ex)
            {
                _lastError = string.Format(Strings.Linux_Typing_Failed, ex.Message);
                return _lastError;
            }
        }
    }

    public int Send(ReadOnlySpan<KeyEvent> events, out int errorCode)
    {
        errorCode = 0;
        // Erst übersetzen (Span darf nicht in den asynchronen Teil)
        var keys = new List<(int Keysym, bool Up)>(events.Length);
        var counts = new List<int>(events.Length);
        for (int i = 0; i < events.Length; i++)
        {
            var e = events[i];
            bool up = (e.Flags & KeyFlags.Up) != 0;
            uint keysym;
            int consumed = 1;
            if ((e.Flags & KeyFlags.Unicode) != 0)
            {
                char c = (char)e.Scan;
                int cp = c;
                if (char.IsHighSurrogate(c) && i + 1 < events.Length && char.IsLowSurrogate((char)events[i + 1].Scan)
                    && (events[i + 1].Flags & KeyFlags.Up) == (e.Flags & KeyFlags.Up))
                {
                    cp = Keysyms.JoinSurrogates(c, (char)events[i + 1].Scan);
                    consumed = 2;
                }
                keysym = Keysyms.FromCodePoint(cp);
            }
            else keysym = e.VirtualKey == KeyboardTyper.VK_MASK ? Keysyms.NoSymbol : Keysyms.FromVirtualKey(e.VirtualKey);
            keys.Add(((int)keysym, up));
            counts.Add(consumed);
            i += consumed - 1;
        }

        lock (_gate)
        {
            int accepted = 0;
            try
            {
                if (_session is null) Run(StartAsync);
                for (int k = 0; k < keys.Count; k++)
                {
                    var (keysym, up) = keys[k];
                    if (keysym != 0) Run(() => NotifyAsync(keysym, up));
                    accepted += counts[k];
                }
                return accepted;
            }
            catch (PortalException ex)
            {
                _lastError = string.Format(Strings.Linux_Typing_Failed, ex.Message);
                DropSession();
                errorCode = 1;
                return accepted;
            }
        }
    }

    public string DescribeRejection() => _lastError ?? Strings.Linux_Typing_PortalRejected;

    private async Task NotifyAsync(int keysym, bool up)
    {
        await _portal!.CallAsync(PortalConnection.ObjectPath, Iface, "NotifyKeyboardKeysym", "oa{sv}iu", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(_session!);
            w.WriteDictionary(new Dictionary<string, VariantValue>());
            w.WriteInt32(keysym);
            w.WriteUInt32(up ? 0u : 1u);
        }).ConfigureAwait(false);
    }

    private async Task StartAsync()
    {
        _portal ??= await PortalConnection.ConnectAsync().ConfigureAwait(false);
        uint version = await _portal.GetVersionAsync(Iface).ConfigureAwait(false);
        if (version == 0)
            throw new PortalException(Strings.Linux_Typing_NoRemoteDesktop);

        var created = await _portal.RequestAsync(Iface, "CreateSession", "a{sv}", null,
            new Dictionary<string, VariantValue> { ["session_handle_token"] = VariantValue.String(PortalConnection.NewToken()) },
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        string session = created.GetString("session_handle")
            ?? throw new PortalException(Strings.Linux_Typing_NoPortalSession);

        var select = new Dictionary<string, VariantValue> { ["types"] = VariantValue.UInt32(DeviceKeyboard) };
        if (version >= 2)
        {
            select["persist_mode"] = VariantValue.UInt32(PersistUntilRevoked);
            if (ReadToken() is { } token) select["restore_token"] = VariantValue.String(token);
        }
        var selected = await _portal.RequestAsync(Iface, "SelectDevices", "oa{sv}", (ref MessageWriter w) => w.WriteObjectPath(session),
            select, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (!selected.Ok) throw new PortalException(Strings.Linux_Typing_KeyboardNotGranted);

        // Start zeigt beim ersten Mal den Dialog „Fernsteuerung erlauben?“ – dem Benutzer Zeit lassen
        var started = await _portal.RequestAsync(Iface, "Start", "osa{sv}", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            w.WriteString("");
        }, [], TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (!started.Ok)
        {
            await _portal.CloseSessionAsync(session).ConfigureAwait(false);
            throw new PortalException(started.Code == 1
                ? Strings.Linux_Typing_RemoteControlDenied
                : Strings.Linux_Typing_RemoteControlCancelled);
        }
        if (started.GetString("restore_token") is { Length: > 0 } restore) SaveToken(restore);
        _session = session;
    }

    private void DropSession()
    {
        var session = _session;
        _session = null;
        if (session is not null && _portal is { } portal) _ = portal.CloseSessionAsync(session);
    }

    private static string? ReadToken()
    {
        try { return File.Exists(TokenPath) ? File.ReadAllText(TokenPath).Trim() is { Length: > 0 } t ? t : null : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void SaveToken(string token)
    {
        try
        {
            Directory.CreateDirectory(LinuxSession.LupeConfigDir);
            File.WriteAllText(TokenPath, token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(TokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Dann fragt der Desktop beim nächsten Start eben wieder
        }
    }

    /// <summary>Führt einen Portal-Aufruf synchron aus (im Thread-Pool, damit kein Synchronisationskontext blockiert).</summary>
    private static void Run(Func<Task> action)
    {
        try
        {
            Task.Run(action).GetAwaiter().GetResult();
        }
        catch (PortalException)
        {
            throw;
        }
        catch (Exception ex) when (ex is DBusExceptionBase or IOException or ObjectDisposedException or InvalidOperationException)
        {
            throw new PortalException(string.Format(Strings.Linux_Portal_ConnectionDisturbed, ex.Message.Trim()), ex);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DropSession();
            _portal?.Dispose();
            _portal = null;
        }
    }
}
