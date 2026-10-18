using System.Security.Cryptography;
using Lupe.Core.Resources;
using Tmds.DBus.Protocol;

namespace Lupe.Platform.Linux.Portal;

/// <summary>Schreibt die Argumente eines Portal-Aufrufs vor dem abschließenden Optionen-Wörterbuch.</summary>
internal delegate void PortalArgs(ref MessageWriter writer);

/// <summary>Antwort einer Portal-Anfrage (org.freedesktop.portal.Request.Response).</summary>
/// <param name="Code">0 = erfolgreich, 1 = vom Benutzer abgelehnt, 2 = anders beendet.</param>
internal sealed record PortalResponse(uint Code, Dictionary<string, VariantValue> Results)
{
    public bool Ok => Code == 0;
    public string? GetString(string key) =>
        Results.TryGetValue(key, out var v) && v.Type == VariantValueType.String ? v.GetString() : null;
}

/// <summary>
/// Verbindung zu xdg-desktop-portal über den Sitzungsbus (Screenshot, RemoteDesktop, GlobalShortcuts). Fehler der
/// Verbindung (kein Bus, kein Portal) kommen als <see cref="PortalException"/> mit deutscher Meldung.
/// </summary>
internal sealed class PortalConnection : IDisposable
{
    public const string Bus = "org.freedesktop.portal.Desktop";
    public const string ObjectPath = "/org/freedesktop/portal/desktop";

    private readonly DBusConnection _connection;

    private PortalConnection(DBusConnection connection) => _connection = connection;

    public DBusConnection Connection => _connection;

    /// <summary>Verbindet mit dem Sitzungsbus; ohne Bus eine <see cref="PortalException"/>.</summary>
    public static async Task<PortalConnection> ConnectAsync(CancellationToken ct = default)
    {
        string? address = DBusAddress.Session;
        if (string.IsNullOrWhiteSpace(address))
            throw new PortalException(Strings.Linux_Portal_NoSessionBus);
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            return new PortalConnection(connection);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException or IOException or System.Net.Sockets.SocketException)
        {
            connection.Dispose();
            throw new PortalException(string.Format(Strings.Linux_Portal_BusConnectFailed, ex.Message.Trim()), ex);
        }
    }

    /// <summary>Version einer Portal-Schnittstelle; 0, wenn das Portal sie nicht anbietet.</summary>
    public async Task<uint> GetVersionAsync(string iface, CancellationToken ct = default)
    {
        var message = Build(ObjectPath, "org.freedesktop.DBus.Properties", "Get", "ss", (ref MessageWriter w) =>
        {
            w.WriteString(iface);
            w.WriteString("version");
        });
        try
        {
            return await _connection.CallMethodAsync(message,
                (Message m, object? _) => m.GetBodyReader().ReadVariantValue().GetUInt32(), null)
                .WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            return 0;
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Ruft eine Portal-Methode auf, die ein Request-Objekt liefert, und wartet auf dessen Antwort. Das Optionen-Wörterbuch
    /// (letztes Argument) bekommt automatisch ein <c>handle_token</c>.
    /// </summary>
    public async Task<PortalResponse> RequestAsync(string iface, string method, string signature, PortalArgs? args,
        Dictionary<string, VariantValue> options, TimeSpan timeout, CancellationToken ct = default)
    {
        string token = NewToken();
        options["handle_token"] = VariantValue.String(token);
        string sender = (_connection.UniqueName ?? "").TrimStart(':').Replace('.', '_');
        string requestPath = $"{ObjectPath}/request/{sender}/{token}";

        var response = new TaskCompletionSource<PortalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Erst auf die Antwort horchen, dann aufrufen – sonst kann sie verloren gehen
        using var watch = await _connection.WatchSignalAsync(Bus, requestPath, "org.freedesktop.portal.Request", "Response",
            (Message m, object? _) =>
            {
                var reader = m.GetBodyReader();
                uint code = reader.ReadUInt32();
                var results = reader.ReadDictionaryOfStringToVariantValue();
                return new PortalResponse(code, results);
            },
            (Notification<PortalResponse> n) =>
            {
                if (n.HasValue) response.TrySetResult(n.Value);
                else if (n.Type != NotificationType.ObserverDisposed)
                    response.TrySetException(new PortalException(Strings.Linux_Portal_ConnectionLost, n.Exception));
            }, ObserverFlags.None, emitOnCapturedContext: false, null).ConfigureAwait(false);

        string handle;
        var message = Build(ObjectPath, iface, method, signature, (ref MessageWriter w) =>
        {
            args?.Invoke(ref w);
            w.WriteDictionary(options);
        });
        try
        {
            handle = await _connection.CallMethodAsync(message,
                (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException ex)
        {
            throw new PortalException(Describe(ex, iface), ex);
        }

        if (handle != requestPath)
        {
            // Ältere Portale vergeben den Pfad selbst
            using var other = await _connection.WatchSignalAsync(Bus, handle, "org.freedesktop.portal.Request", "Response",
                (Message m, object? _) =>
                {
                    var reader = m.GetBodyReader();
                    return new PortalResponse(reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
                },
                (Notification<PortalResponse> n) =>
                {
                    if (n.HasValue) response.TrySetResult(n.Value);
                }, ObserverFlags.None, emitOnCapturedContext: false, null).ConfigureAwait(false);
            return await WaitAsync(response.Task, timeout, ct).ConfigureAwait(false);
        }
        return await WaitAsync(response.Task, timeout, ct).ConfigureAwait(false);
    }

    /// <summary>Ruft eine Methode ohne Request-Objekt auf (z. B. NotifyKeyboardKeysym).</summary>
    public async Task CallAsync(string path, string iface, string method, string signature, PortalArgs args, CancellationToken ct = default)
    {
        var message = Build(path, iface, method, signature, args);
        try
        {
            await _connection.CallMethodAsync(message).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException ex)
        {
            throw new PortalException(Describe(ex, iface), ex);
        }
    }

    /// <summary>Schließt eine Portal-Sitzung (org.freedesktop.portal.Session.Close), Fehler egal.</summary>
    public async Task CloseSessionAsync(string sessionPath)
    {
        try
        {
            var message = Build(sessionPath, "org.freedesktop.portal.Session", "Close", null, null);
            await _connection.CallMethodAsync(message).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sitzung war schon zu oder der Bus ist weg
        }
    }

    /// <summary>Baut einen Methodenaufruf an das Portal (synchron – MessageWriter darf kein await überdauern).</summary>
    private MessageBuffer Build(string path, string iface, string member, string? signature, PortalArgs? args)
    {
        var writer = _connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(Bus, path, iface, member, signature, MessageFlags.None);
            args?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    public static string NewToken() => "lupe" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    private static async Task<PortalResponse> WaitAsync(Task<PortalResponse> task, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            return await task.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new PortalException(Strings.Linux_Portal_Timeout, ex);
        }
    }

    private static string Describe(DBusErrorReplyException ex, string iface)
    {
        string what = iface[(iface.LastIndexOf('.') + 1)..];
        return ex.ErrorName switch
        {
            "org.freedesktop.DBus.Error.ServiceUnknown" =>
                Strings.Linux_Portal_NotRunning,
            "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownInterface" =>
                string.Format(Strings.Linux_Portal_NotOffered, what),
            "org.freedesktop.DBus.Error.AccessDenied" or "org.freedesktop.portal.Error.NotAllowed" =>
                string.Format(Strings.Linux_Portal_Denied, what),
            _ => string.Format(Strings.Linux_Portal_Error, what, ex.ErrorMessage),
        };
    }

    public void Dispose() => _connection.Dispose();
}

/// <summary>Ein Portal ist nicht erreichbar oder hat abgelehnt; <see cref="Exception.Message"/> ist deutsch.</summary>
internal sealed class PortalException(string message, Exception? inner = null) : Exception(message, inner);
